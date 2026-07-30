using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Imdb;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Microsoft.Extensions.Logging;
using TmdbPerson = TMDbLib.Objects.People.Person;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Imdb;

/// <summary>
/// Follows the filmographies of the people named on an IMDb people list, emitting the credits the library
/// does not own as <see cref="GapPattern.CreatorWorks"/> gaps.
/// </summary>
/// <remarks>
/// This is the one creator source that is not seeded from the library. <see cref="Tmdb.PeopleGapSource"/> and
/// the Trakt cross-check both start from a person already attached to an owned item, so there is otherwise no
/// way to follow a director you own nothing by. A curated IMDb list is exactly that input, already maintained
/// by hand. It reads the same <see cref="PluginConfiguration.ImdbListIds"/> field the titles source reads and
/// takes the PEOPLE lists out of it, so one list of ids covers both and (through the shared cache) one fetch
/// feeds both.
/// <para>
/// Every person on every list is read, but only a batch is resolved per run, stalest-first through
/// <see cref="ScanCursorStore"/>, the same rotation <see cref="Tmdb.PeopleGapSource"/> uses. A person costs
/// two cached TMDB calls and yields a whole filmography, so resolving a 250-name list in one pass is not
/// reasonable; taking the first N every run instead would mean the tail of a long list was never seen at all.
/// The engine carries unowned gaps forward between runs, so coverage accumulates rather than churning.
/// </para>
/// </remarks>
internal sealed class ImdbPeopleListGapSource : IGapSource
{
    // How many people are resolved per run. Not a bound on what is seen: the rest are picked up by the next
    // run, oldest first.
    private const int PeoplePerRun = 50;

    // How many names are read off one list. Far above any hand-curated list, and only a bound on the read,
    // not on what is resolved.
    private const int MaxNamesPerList = 1000;

    private readonly ImdbClient _imdb;
    private readonly TmdbClient _tmdb;
    private readonly ScanCursorStore _cursors;
    private readonly ResolutionStore _resolutions;
    private readonly ILogger<ImdbPeopleListGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImdbPeopleListGapSource"/> class.
    /// </summary>
    /// <param name="imdb">The IMDb client.</param>
    /// <param name="tmdb">The TMDB client, for resolving each person and reading their credits.</param>
    /// <param name="cursors">Tracks which people have been resolved this cycle, for cross-run backfill.</param>
    /// <param name="resolutions">Holds dismissals, including whole-creator dismissals to skip.</param>
    /// <param name="logger">The logger.</param>
    public ImdbPeopleListGapSource(
        ImdbClient imdb,
        TmdbClient tmdb,
        ScanCursorStore cursors,
        ResolutionStore resolutions,
        ILogger<ImdbPeopleListGapSource> logger)
    {
        _imdb = imdb;
        _tmdb = tmdb;
        _cursors = cursors;
        _resolutions = resolutions;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "IMDb people lists";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie, BaseItemKind.Series };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanImdbPeopleLists && ImdbListInput.ParseIds(config.ImdbListIds).Count > 0;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var candidates = await ReadPeopleAsync(context, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            yield break;
        }

        // Drop rotation entries for people no longer on any configured list, so the table tracks the live set.
        _cursors.RetainOnly(Name, candidates.Select(c => c.ImdbNameId).ToHashSet(StringComparer.Ordinal));

        var dismissed = DismissedCreatorIds();
        var lastScanned = _cursors.GetLastScanned(Name);
        var ordered = candidates
            .Where(c => !dismissed.Contains(SourceItemIds.ImdbPerson(c.ImdbNameId)))
            .OrderByStalest(lastScanned, c => c.ImdbNameId)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var batch = ordered.Count > PeoplePerRun ? ordered.GetRange(0, PeoplePerRun) : ordered;
        if (ordered.Count > batch.Count)
        {
            _logger.LogInformation(
                "IMDb people: {Batch} of {Total} people this run, stalest first; the rest follow on later runs",
                batch.Count,
                ordered.Count);
        }

        var language = context.Config.MetadataLanguage;
        var country = context.Config.MetadataCountryCode;
        var scannedKeys = new List<string>(batch.Count);

        for (var index = 0; index < batch.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index / Math.Max(1, batch.Count));

            var candidate = batch[index];
            scannedKeys.Add(candidate.ImdbNameId);

            var person = await ResolvePersonAsync(candidate, language, country, cancellationToken).ConfigureAwait(false);
            if (person is null)
            {
                continue;
            }

            var gaps = FilmographyGapMapper.Build(
                person,
                SourceItemIds.ImdbPerson(candidate.ImdbNameId),
                candidate.Name,
                context.Ownership,
                _tmdb.GetPosterUrl,
                context.Config.MinFilmographyVotes,
                context.Config.MaxCastBillingOrder);

            foreach (var gap in gaps)
            {
                yield return gap;
            }
        }

        // Marked only after the batch runs, so an aborted scan re-tries the same people rather than skipping
        // them for a whole cycle.
        _cursors.MarkScanned(Name, scannedKeys);
    }

    // Every person named across the configured people lists, de-duplicated: the same director on two lists is
    // one filmography, not two.
    private async Task<List<PersonCandidate>> ReadPeopleAsync(GapScanContext context, CancellationToken cancellationToken)
    {
        var candidates = new List<PersonCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in ImdbListInput.ParseIds(context.Config.ImdbListIds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ServiceCircuit.IsOpen(ServiceNames.Imdb))
            {
                _logger.LogWarning("IMDb people: service unavailable this run; skipping the remaining lists");
                break;
            }

            ImdbListContents? list;
            try
            {
                list = await _imdb.GetListAsync(id, MaxNamesPerList, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "IMDb people: failed to read {Id}", id);
                continue;
            }

            // A titles list is the other source's input. Deciding on the served list type rather than on
            // "no names found" keeps a reachable-but-empty people list distinguishable in the log.
            if (list is null || !string.Equals(list.ListType, ImdbListContents.PeopleType, StringComparison.Ordinal))
            {
                continue;
            }

            var added = 0;
            foreach (var entry in list.Names)
            {
                if (entry.Id is { Length: > 0 } nameId
                    && entry.NameText?.Value is { Length: > 0 } personName
                    && seen.Add(nameId))
                {
                    candidates.Add(new PersonCandidate(nameId, personName));
                    added++;
                }
            }

            _logger.LogInformation("IMDb people: list '{Name}' ({Id}) names {Count} people", list.Name, list.Id, added);
        }

        return candidates;
    }

    private async Task<TmdbPerson?> ResolvePersonAsync(
        PersonCandidate candidate,
        string? language,
        string? country,
        CancellationToken cancellationToken)
    {
        try
        {
            var tmdbId = await _tmdb.FindPersonByImdbIdAsync(candidate.ImdbNameId, cancellationToken).ConfigureAwait(false);
            if (tmdbId is null)
            {
                _logger.LogInformation(
                    "IMDb people: TMDB does not know {Name} ({ImdbId}); skipping",
                    candidate.Name,
                    candidate.ImdbNameId);
                return null;
            }

            return await _tmdb.GetPersonAsync(tmdbId.Value, language, country, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "IMDb people: failed to read the filmography of {Name} ({ImdbId})",
                candidate.Name,
                candidate.ImdbNameId);
            return null;
        }
    }

    // The owner ids the user dismissed as a whole creator.
    private HashSet<string> DismissedCreatorIds()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in _resolutions.GetAll().Keys)
        {
            if (id.StartsWith(GapResolution.CreatorPrefix, StringComparison.Ordinal))
            {
                set.Add(id[GapResolution.CreatorPrefix.Length..]);
            }
        }

        return set;
    }

    private sealed record PersonCandidate(string ImdbNameId, string Name);
}
