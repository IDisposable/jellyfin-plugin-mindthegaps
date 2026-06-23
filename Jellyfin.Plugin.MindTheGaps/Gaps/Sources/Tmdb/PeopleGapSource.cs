using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using TmdbPerson = TMDbLib.Objects.People.Person;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Finds movies and series from an owned person's filmography (TMDB person credits) that are missing
/// from the library. Covers acting roles and key crew jobs (director/writer).
/// </summary>
public sealed class PeopleGapSource : IGapSource
{
    // Fallback cap when the configured value is not set; TMDB person fetch is one cached call each.
    private const int DefaultMaxPeople = 1000;

    private readonly ILibraryManager _libraryManager;
    private readonly TmdbClient _tmdb;
    private readonly ScanCursorStore _cursors;
    private readonly ResolutionStore _resolutions;
    private readonly ILogger<PeopleGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeopleGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="cursors">Tracks which people have been scanned this cycle, for cross-run backfill.</param>
    /// <param name="resolutions">Holds dismissals, including whole-creator dismissals to skip.</param>
    /// <param name="logger">The logger.</param>
    public PeopleGapSource(
        ILibraryManager libraryManager,
        TmdbClient tmdb,
        ScanCursorStore cursors,
        ResolutionStore resolutions,
        ILogger<PeopleGapSource> logger)
    {
        _libraryManager = libraryManager;
        _tmdb = tmdb;
        _cursors = cursors;
        _resolutions = resolutions;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Filmography";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie, BaseItemKind.Series };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.ScanPeople;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var language = context.Config.MetadataLanguage;
        var country = context.Config.MetadataCountryCode;
        var cap = context.Config.MaxFilmographyPeople > 0 ? context.Config.MaxFilmographyPeople : DefaultMaxPeople;

        var people = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Person },
            Recursive = true
        });

        // Order people stalest-first (never-scanned rank as oldest), tiebroken by relevance (most owned
        // credits) then name. Each run takes the next cap, so over runs the whole cast and crew is covered
        // and then the people scanned longest ago refresh; the engine carries unowned gaps forward between
        // runs. Creators the user dismissed wholesale are excluded entirely.
        var dismissed = DismissedCreatorGuids();
        var lastScanned = _cursors.GetLastScanned(Name);
        var appearances = CountOwnedAppearances(cancellationToken);

        var tagged = people
            .Select(p => (Person: p, Key: p.Id.ToString("N", CultureInfo.InvariantCulture)))
            .ToList();

        // Drop rotation entries for people no longer in the library, so the table tracks the live cast.
        _cursors.RetainOnly(Name, tagged.Select(x => x.Key).ToHashSet(StringComparer.Ordinal));

        var ordered = tagged
            .Where(x => !dismissed.Contains(x.Key))
            .OrderBy(x => lastScanned.TryGetValue(x.Key, out var t) ? t : DateTime.MinValue)
            .ThenByDescending(x => appearances.TryGetValue(x.Person.Name, out var c) ? c : 0)
            .ThenBy(x => x.Person.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var batch = ordered.Count > cap ? ordered.GetRange(0, cap) : ordered;

        var scannedKeys = new List<string>(batch.Count);
        for (var index = 0; index < batch.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index / Math.Max(1, batch.Count));

            var person = batch[index].Person;
            scannedKeys.Add(batch[index].Key);

            if (!person.TryGetProviderId(MetadataProvider.Tmdb, out var idStr)
                || !int.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var personTmdbId))
            {
                continue;
            }

            TmdbPerson? tmdbPerson = null;
            try
            {
                tmdbPerson = await _tmdb
                    .GetPersonAsync(personTmdbId, language, country, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Filmography: failed to fetch TMDB person {Id} ({Name})", personTmdbId, person.Name);
            }

            if (tmdbPerson is null)
            {
                continue;
            }

            var gaps = FilmographyGapMapper.Build(
                tmdbPerson,
                person.Id.ToString("N", CultureInfo.InvariantCulture),
                person.Name,
                context.Ownership,
                _tmdb.GetPosterUrl,
                context.Config.MinFilmographyVotes,
                context.Config.MaxCastBillingOrder);

            foreach (var gap in gaps)
            {
                yield return gap;
            }
        }

        _cursors.MarkScanned(Name, scannedKeys);
    }

    // The set of owned-person guids (N-format) the user dismissed as a whole creator.
    private HashSet<string> DismissedCreatorGuids()
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

    // Count how many owned movies/series credit each person by name. Used as the relevance tiebreak when
    // ordering people, so the per-run cap favours the creators the library has the most work from.
    private Dictionary<string, int> CountOwnedAppearances(CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var owned = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true
        });

        foreach (var item in owned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var credit in _libraryManager.GetPeople(item))
            {
                if (string.IsNullOrEmpty(credit.Name))
                {
                    continue;
                }

                counts.TryGetValue(credit.Name, out var current);
                counts[credit.Name] = current + 1;
            }
        }

        return counts;
    }
}
