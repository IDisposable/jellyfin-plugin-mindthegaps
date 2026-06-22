using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Shared logic for the series-completeness cross-checks. For each owned series it asks an
/// external source (TVmaze, TheTVDB) for the canonical episode list and reports the regular
/// episodes the library doesn't have on disk. Gap ids match the library reader, so duplicates from
/// different sources collapse to one todo entry.
/// </summary>
public abstract class SeriesContentGapSourceBase : IGapSource
{
    // The largest plausible gap between an owned series' start year and the first season of the show resolved
    // for it. Beyond this, the resolved show is almost certainly a same-named reboot, not the same series.
    private const int RebootYearGap = 3;

    private readonly ILibraryManager _libraryManager;
    private readonly ScanCursorStore _cursors;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesContentGapSourceBase"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="cursors">Tracks which series were cross-checked, for stalest-first rotation.</param>
    /// <param name="logger">The logger.</param>
    protected SeriesContentGapSourceBase(ILibraryManager libraryManager, ScanCursorStore cursors, ILogger logger)
    {
        _libraryManager = libraryManager;
        _cursors = cursors;
        _logger = logger;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    // Reads the library directly per series, so it needs nothing in the ownership index.
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = Array.Empty<BaseItemKind>();

    /// <summary>
    /// Gets the maximum number of series to hit the external API for in a single run.
    /// </summary>
    protected abstract int MaxSeries { get; }

    /// <summary>
    /// Gets the service name this source calls (for example "TVmaze"), so it can stop once that service's
    /// circuit has opened (it has been given up on for the run).
    /// </summary>
    protected abstract string ServiceName { get; }

    /// <inheritdoc />
    public abstract bool IsEnabled(PluginConfiguration config);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var allSeries = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Series },
            Recursive = true
        });

        // 0 in config means "no limit".
        var episodeCap = context.Config.MaxMissingEpisodesPerShow;
        if (episodeCap <= 0)
        {
            episodeCap = int.MaxValue;
        }

        // Only series this source can resolve are candidates (the rest cost no API call). Rotate them
        // stalest-first (never-checked first), so over repeated runs every series is cross-checked and
        // then the longest-unchecked refresh, rather than always re-checking the first MaxSeries in
        // library order and never reaching the tail. The engine carries cross-check-only episode gaps
        // forward between runs so coverage accumulates. Prune entries for series no longer present.
        var candidates = new List<(BaseItem Series, string Key)>();
        foreach (var series in allSeries)
        {
            if (HasLookupId(series))
            {
                candidates.Add((series, series.Id.ToString("N", CultureInfo.InvariantCulture)));
            }
        }

        _cursors.RetainOnly(Name, candidates.Select(c => c.Key).ToHashSet(StringComparer.Ordinal));
        var lastScanned = _cursors.GetLastScanned(Name);

        var ordered = candidates
            .OrderBy(c => lastScanned.TryGetValue(c.Key, out var t) ? t : DateTime.MinValue)
            .ThenBy(c => c.Series.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var batch = ordered.Count > MaxSeries ? ordered.GetRange(0, MaxSeries) : ordered;
        if (ordered.Count > MaxSeries)
        {
            _logger.LogInformation("{Source}: cross-checking {Batch} of {Total} resolvable series this run (stalest first)", Name, MaxSeries, ordered.Count);
        }

        var scannedKeys = new List<string>(batch.Count);
        for (var index = 0; index < batch.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index / Math.Max(1, batch.Count));

            // The service has been given up on for this run (its circuit is open); each remaining series would
            // only fast-fail, so stop here. The series not marked scanned stay stalest, so next run retries them.
            if (ServiceCircuit.IsOpen(ServiceName))
            {
                _logger.LogInformation("{Source}: {Service} is unavailable this run; skipping the remaining series", Name, ServiceName);
                break;
            }

            var series = batch[index].Series;
            scannedKeys.Add(batch[index].Key);

            var canonical = await GetCanonicalEpisodesAsync(series, context, cancellationToken).ConfigureAwait(false);
            if (canonical is null || canonical.Count == 0)
            {
                continue;
            }

            // Guard against resolving a same-named reboot. If the owned series has a year but the show that
            // was resolved for it has its first season airing in a very different year (V 1984 versus V 2009),
            // it is a different series, so do not report that show's seasons as missing episodes of this one.
            if (LooksLikeDifferentSeries(series.ProductionYear, canonical))
            {
                _logger.LogInformation(
                    "{Source}: the show resolved for {Series} ({Year}) starts in a very different year, so it looks like a same-named reboot; skipping it to avoid false missing episodes",
                    Name,
                    series.Name,
                    series.ProductionYear);
                continue;
            }

            var owned = GetOwnedEpisodeNumbers(series.Id);
            foreach (var episode in SeriesContentDiff.Missing(canonical, owned, episodeCap))
            {
                yield return BuildGap(series, episode);
            }
        }

        _cursors.MarkScanned(Name, scannedKeys);
    }

    /// <summary>
    /// Determines whether the series carries an id this source can resolve, without any network call.
    /// </summary>
    /// <param name="series">The owned series.</param>
    /// <returns><see langword="true"/> if this source can attempt a lookup.</returns>
    protected abstract bool HasLookupId(BaseItem series);

    /// <summary>
    /// Fetches the canonical episode list for an owned series from the external source.
    /// </summary>
    /// <param name="series">The owned series.</param>
    /// <param name="context">The scan context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The canonical episodes, or <see langword="null"/> if the series could not be resolved.</returns>
    protected abstract Task<IReadOnlyList<CanonicalEpisode>?> GetCanonicalEpisodesAsync(
        BaseItem series,
        GapScanContext context,
        CancellationToken cancellationToken);

    // True when the owned series has a year and the resolved canonical list's lowest season aired far enough
    // from it to be a different, same-named series. A correct resolution has season one airing within a year
    // or two of the series' start year, whatever later seasons do, so this never rejects a legitimate long run
    // (it compares the lowest season's year to the start year, not every episode's year).
    internal static bool LooksLikeDifferentSeries(int? seriesYear, IReadOnlyList<CanonicalEpisode> canonical)
    {
        if (seriesYear is not int year)
        {
            return false;
        }

        int? lowestSeasonYear = null;
        var lowestSeason = int.MaxValue;
        foreach (var episode in canonical)
        {
            if (episode.Season < 1 || episode.ReleaseDate is not { } aired)
            {
                continue;
            }

            if (episode.Season < lowestSeason || (episode.Season == lowestSeason && aired.Year < lowestSeasonYear))
            {
                lowestSeason = episode.Season;
                lowestSeasonYear = aired.Year;
            }
        }

        return lowestSeasonYear is int first && Math.Abs(first - year) > RebootYearGap;
    }

    private static GapItem BuildGap(BaseItem series, CanonicalEpisode episode)
    {
        var code = string.Create(CultureInfo.InvariantCulture, $"S{episode.Season:D2}E{episode.Number:D2}");
        var name = string.IsNullOrEmpty(episode.Name)
            ? string.Create(CultureInfo.InvariantCulture, $"{series.Name} {code}")
            : string.Create(CultureInfo.InvariantCulture, $"{series.Name} {code} - {episode.Name}");

        return GapItemFactory.Create(
            id: SeriesGapKey.Episode(series.Id, episode.Season, episode.Number),
            pattern: GapPattern.SetCompletion,
            domain: MediaDomain.Shows,
            targetKind: BaseItemKind.Episode,
            name: name,
            providerIds: new Dictionary<string, string>(),
            sourceItemId: series.Id.ToString("N", CultureInfo.InvariantCulture),
            sourceItemName: series.Name,
            sourceItemType: "Series",
            releaseDate: episode.ReleaseDate,
            overview: episode.Overview,
            season: episode.Season,
            sourceItemYear: series.ProductionYear);
    }

    private HashSet<(int Season, int Number)> GetOwnedEpisodeNumbers(Guid seriesId)
    {
        var owned = new HashSet<(int Season, int Number)>();
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = new[] { seriesId },
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            if (item is Episode episode
                && episode.ParentIndexNumber is int season
                && episode.IndexNumber is int number)
            {
                owned.Add((season, number));
            }
        }

        return owned;
    }
}
