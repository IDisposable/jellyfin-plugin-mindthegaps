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
/// Finds movies from an owned person's filmography (TMDB person credits) that are missing
/// from the library. Covers acting roles and key crew jobs (director/writer).
/// </summary>
public sealed class PeopleGapSource : IGapSource
{
    // Fallback cap when the configured value is not set; TMDB person fetch is one cached call each.
    private const int DefaultMaxPeople = 1000;

    private readonly ILibraryManager _libraryManager;
    private readonly TmdbClient _tmdb;
    private readonly ScanCursorStore _cursors;
    private readonly ILogger<PeopleGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeopleGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="cursors">Tracks which people have been scanned this cycle, for cross-run backfill.</param>
    /// <param name="logger">The logger.</param>
    public PeopleGapSource(
        ILibraryManager libraryManager,
        TmdbClient tmdb,
        ScanCursorStore cursors,
        ILogger<PeopleGapSource> logger)
    {
        _libraryManager = libraryManager;
        _tmdb = tmdb;
        _cursors = cursors;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Filmography";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie };

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

        // Order people by how many owned movies/series credit them, so each run's batch is the most
        // relevant un-scanned creators, not the alphabetically-first names.
        var ordered = OrderByRelevance(people, cancellationToken);

        // Resume where the last run left off: take the next batch of people not yet scanned this cycle, so
        // over repeated runs the whole cast and crew is covered (the engine carries unowned filmography
        // gaps forward between runs). When everyone has been scanned, start a fresh cycle to refresh.
        var done = new HashSet<string>(_cursors.GetProcessed(Name), StringComparer.Ordinal);
        var batch = new List<BaseItem>();
        foreach (var candidate in ordered)
        {
            if (batch.Count >= cap)
            {
                break;
            }

            if (!done.Contains(candidate.Id.ToString("N", CultureInfo.InvariantCulture)))
            {
                batch.Add(candidate);
            }
        }

        if (batch.Count == 0 && ordered.Count > 0)
        {
            _logger.LogInformation("Filmography: all {Count} people scanned this cycle; starting a fresh cycle", ordered.Count);
            _cursors.StartNewCycle(Name);
            foreach (var candidate in ordered)
            {
                if (batch.Count >= cap)
                {
                    break;
                }

                batch.Add(candidate);
            }
        }

        var scannedKeys = new List<string>(batch.Count);
        for (var index = 0; index < batch.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index / Math.Max(1, batch.Count));

            var person = batch[index];
            scannedKeys.Add(person.Id.ToString("N", CultureInfo.InvariantCulture));

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
                _tmdb.GetPosterUrl);

            foreach (var gap in gaps)
            {
                yield return gap;
            }
        }

        _cursors.MarkProcessed(Name, scannedKeys);
    }

    // Order owned people most-credited-first. Counts how many owned movies/series list each person, then
    // sorts the person list by that count (descending), tie-breaking by name. This makes the per-run cap
    // keep the prominent creators rather than the alphabetically-first names.
    private List<BaseItem> OrderByRelevance(IReadOnlyList<BaseItem> people, CancellationToken cancellationToken)
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

        return people
            .OrderByDescending(p => counts.TryGetValue(p.Name, out var c) ? c : 0)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
