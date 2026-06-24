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
using Jellyfin.Plugin.MindTheGaps.Services;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// The single series-completeness source. For each owned series it asks every reachable episode provider
/// (TheMovieDb, TheTVDB, TVmaze) for its canonical episode list, orders them by the Shows library's metadata
/// fetcher preference, and merges by season (<see cref="SeriesContentMerge"/>): the highest-ranked provider
/// owns each season it lists, a lower provider can add a season none above it has, and the library's own
/// virtual (missing) episodes are the last-chance list for seasons no provider opined on. The merged list is
/// reconciled against the owned episodes (by number, air date, and folded title) and the difference is
/// reported. A missing episode the server already tracks as a virtual item is linked to it; one only a
/// provider knows about is reported lean. Series no external provider can resolve are surfaced in bulk from
/// their virtual episodes alone, so a large library's missing episodes appear every run regardless of the
/// per-run cap on the providers' (rate-limited) cross-checks.
/// </summary>
public sealed class SeriesContentGapSource : IGapSource, ISeriesContentSource
{
    // The largest plausible gap between an owned series' start year and a provider's first season for it.
    // Beyond this the provider almost certainly resolved a same-named reboot, not the same series.
    private const int RebootYearGap = 3;

    // The number of provider-resolvable series to cross-check (hit the providers' APIs for) in one run.
    private const int MaxSeries = 300;

    private readonly ILibraryManager _libraryManager;
    private readonly IReadOnlyList<ISeriesEpisodeProvider> _providers;
    private readonly ScanCursorStore _cursors;
    private readonly ILogger<SeriesContentGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesContentGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="providers">The episode providers to merge for each series.</param>
    /// <param name="cursors">Tracks which series were cross-checked, for stalest-first rotation.</param>
    /// <param name="logger">The logger.</param>
    public SeriesContentGapSource(
        ILibraryManager libraryManager,
        IEnumerable<ISeriesEpisodeProvider> providers,
        ScanCursorStore cursors,
        ILogger<SeriesContentGapSource> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _libraryManager = libraryManager;
        _providers = providers.ToList();
        _cursors = cursors;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Series content";

    /// <inheritdoc />
    // Reads the library directly per series, so it needs nothing in the ownership index.
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = [];

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.ScanSeries;

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

        // Partition by whether some external provider can cross-check the series (the library lists it as a
        // fetcher, it has its credentials, and the series carries an id it resolves by). The rest are surfaced
        // from their virtual episodes alone, in bulk.
        var resolvable = new List<(BaseItem Series, string Key)>();
        var libraryOnly = new HashSet<Guid>();
        foreach (var series in allSeries)
        {
            var order = SeriesContentPriority.FetcherOrder(series, _libraryManager);
            if (_providers.Any(p => p.CanResolve(series, context.Config) && LibraryUses(p, order)))
            {
                resolvable.Add((series, series.Id.ToString("N", CultureInfo.InvariantCulture)));
            }
            else
            {
                libraryOnly.Add(series.Id);
            }
        }

        // The library-only series in one bulk pass (cheap, no API), so their missing episodes are reported
        // every run no matter how the provider batch below is capped.
        foreach (var gap in BulkLibraryGaps(libraryOnly, context, cancellationToken))
        {
            yield return gap;
        }

        // The provider-resolvable series, merged per series, stalest first and capped (the providers are
        // rate-limited). Series past the cap keep their stalest rank and their carried-forward gaps until a
        // later run reaches them.
        _cursors.RetainOnly(Name, resolvable.Select(c => c.Key).ToHashSet(StringComparer.Ordinal));
        var lastScanned = _cursors.GetLastScanned(Name);
        var ordered = resolvable
            .OrderByStalest(lastScanned, c => c.Key)
            .ThenBy(c => c.Series.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var batch = ordered.Count > MaxSeries ? ordered.GetRange(0, MaxSeries) : ordered;
        if (ordered.Count > MaxSeries)
        {
            _logger.LogInformation("Series content: cross-checking {Batch} of {Total} provider-resolvable series this run (stalest first)", MaxSeries, ordered.Count);
        }

        var scannedKeys = new List<string>(batch.Count);
        for (var index = 0; index < batch.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index / Math.Max(1, batch.Count));

            var series = batch[index].Series;
            scannedKeys.Add(batch[index].Key);

            foreach (var gap in await CheckSeriesAsync(series, context, cancellationToken).ConfigureAwait(false))
            {
                yield return gap;
            }
        }

        _cursors.MarkScanned(Name, scannedKeys);
    }

    /// <summary>
    /// Re-checks one owned series: merges every reachable provider's episode list with the library's own
    /// virtual episodes and reports the difference against the owned episodes. The per-series step the full
    /// scan loops over, exposed for a targeted re-check.
    /// </summary>
    /// <param name="series">The owned series.</param>
    /// <param name="context">The scan context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The series' missing episodes as gaps, empty when it cannot be resolved or looks like a reboot.</returns>
    public async Task<IReadOnlyList<GapItem>> CheckSeriesAsync(BaseItem series, GapScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Config.ScanSeries)
        {
            return [];
        }

        // The reachable providers, highest library-preference first; a service whose circuit is open is skipped.
        var order = SeriesContentPriority.FetcherOrder(series, _libraryManager);
        var providers = _providers
            .Where(p => !ServiceCircuit.IsOpen(p.ServiceName) && p.CanResolve(series, context.Config) && LibraryUses(p, order))
            .OrderBy(p => SeriesContentPriority.Rank(order, p.Provider))
            .ToList();

        var lists = new List<IReadOnlyList<CanonicalEpisode>>();
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = await provider.GetCanonicalEpisodesAsync(series, context, cancellationToken).ConfigureAwait(false);

            // Guard against a provider resolving a same-named reboot (V 1984 versus V 2009): if its lowest
            // season aired far from the owned series' year, drop its list rather than report a reboot's seasons.
            if (list is { Count: > 0 } && !LooksLikeDifferentSeries(series.ProductionYear, list))
            {
                lists.Add(list);
            }
        }

        var view = BuildLibraryView(series.Id);
        if (view.LastChance.Count > 0)
        {
            lists.Add(view.LastChance);
        }

        if (lists.Count == 0)
        {
            return [];
        }

        var merged = SeriesContentMerge.Combine(lists);

        // The uncapped count feeds the coverage badge ("59 of 62 owned"); the per-show cap only truncates rows.
        var allMissing = SeriesContentDiff.Missing(merged, view.Owned, int.MaxValue);
        var totalCount = view.OwnedCount + allMissing.Count;
        var cap = context.Config.MaxMissingEpisodesPerShow <= 0 ? int.MaxValue : context.Config.MaxMissingEpisodesPerShow;
        var seriesTmdb = series.ProviderIdOrNull(ProviderIds.Tmdb);

        var gaps = new List<GapItem>();
        foreach (var episode in allMissing)
        {
            if (gaps.Count >= cap)
            {
                break;
            }

            // Link to the server's own virtual item when it has one for this episode (so the report opens it
            // and its season directly); otherwise the episode is one only a provider knows about, reported lean.
            gaps.Add(view.VirtualByKey.TryGetValue((episode.Season, episode.Number), out var item)
                ? BuildGap(item, series.ProductionYear, seriesTmdb, view.OwnedCount, totalCount)
                : BuildLeanGap(series, episode, seriesTmdb, view.OwnedCount, totalCount));
        }

        return gaps;
    }

    // A provider is one the library uses when it lists the provider as a metadata fetcher (ranked by that
    // order). When the library lists no fetcher order at all, every credentialed provider is used, so a library
    // that never configured its fetchers is still cross-checked rather than getting nothing.
    private static bool LibraryUses(ISeriesEpisodeProvider provider, IReadOnlyList<KnownProvider?> order)
        => order.Count == 0 || SeriesContentPriority.Rank(order, provider.Provider) < order.Count;

    // True when the owned series has a year and the provider list's lowest season aired far enough from it to
    // be a different, same-named series. Compares the lowest season's year to the start year, so it never
    // rejects a legitimate long run (a later season airing decades on is fine).
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

    // The owned (non-virtual) episodes reconciled against, plus the era-bounded virtual episodes as the
    // last-chance list and a lookup of the virtual item per (season, number) for linking a reported gap.
    private (OwnedEpisodes Owned, int OwnedCount, IReadOnlyList<CanonicalEpisode> LastChance, IReadOnlyDictionary<(int Season, int Number), Episode> VirtualByKey) BuildLibraryView(Guid seriesId)
    {
        var owned = new OwnedEpisodes();
        var ownedCount = 0;
        (int Min, int Max)? ownedRange = null;
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = new[] { seriesId },
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            if (item is not Episode episode || episode.ParentIndexNumber is not int season)
            {
                continue;
            }

            ownedCount++;
            if (episode.IndexNumber is int number)
            {
                // One file can span several episodes (S01E01-E02), so own every number in the span.
                var last = episode.IndexNumberEnd is int end && end > number ? end : number;
                for (var n = number; n <= last; n++)
                {
                    owned.AddNumber(season, n);
                }
            }

            if (episode.PremiereDate is { } aired)
            {
                owned.AddAirDate(aired);
            }

            owned.AddTitle(season, episode.Name);

            if (YearOf(episode) is { } y)
            {
                ownedRange = ownedRange is { } r ? (Math.Min(r.Min, y), Math.Max(r.Max, y)) : (y, y);
            }
        }

        var virtuals = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = new[] { seriesId },
            IsMissing = true,
            Recursive = true
        });

        // Expand the owned run through the missing years into the episode era, so an earlier or later season
        // of a long run you only partly own stays listed and only a reboot-sized outlier is dropped.
        (int Min, int Max)? era = null;
        if (ownedRange is { } range)
        {
            var missingYears = virtuals.OfType<Episode>().Select(YearOf).OfType<int>().ToList();
            era = EpisodeEra.Expand(range, missingYears);
        }

        var lastChance = new List<CanonicalEpisode>();
        var byKey = new Dictionary<(int Season, int Number), Episode>();
        foreach (var item in virtuals)
        {
            if (item is not Episode episode
                || episode.ParentIndexNumber is not int season
                || episode.IndexNumber is not int number
                || EpisodeEra.IsOutside(YearOf(episode), era))
            {
                continue;
            }

            byKey[(season, number)] = episode;
            lastChance.Add(new CanonicalEpisode(season, number, episode.Name, episode.PremiereDate, episode.Overview));
        }

        return (owned, ownedCount, lastChance, byKey);
    }

    // The library-only series (no external provider can resolve them) surfaced in one bulk pass from their
    // virtual episodes, reboot outliers excluded, capped per show. Mirrors the per-series path's gaps so a
    // series that later gains a provider id reports the same ids.
    private List<GapItem> BulkLibraryGaps(HashSet<Guid> libraryOnly, GapScanContext context, CancellationToken cancellationToken)
    {
        var gaps = new List<GapItem>();
        if (libraryOnly.Count == 0)
        {
            return gaps;
        }

        var missing = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            IsMissing = true,
            Recursive = true
        });

        // Owned counts and air-year range per series, from the real episodes, to seed each series' era.
        var ownedPerSeries = new Dictionary<Guid, int>();
        var ownedYearRange = new Dictionary<Guid, (int Min, int Max)>();
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            if (item is not Episode ep || !libraryOnly.Contains(ep.SeriesId))
            {
                continue;
            }

            ownedPerSeries.TryGetValue(ep.SeriesId, out var c);
            ownedPerSeries[ep.SeriesId] = c + 1;

            if (YearOf(ep) is { } y)
            {
                ownedYearRange[ep.SeriesId] = ownedYearRange.TryGetValue(ep.SeriesId, out var r)
                    ? (Math.Min(r.Min, y), Math.Max(r.Max, y))
                    : (y, y);
            }
        }

        var missingYearsPerSeries = new Dictionary<Guid, List<int>>();
        foreach (var item in missing)
        {
            if (item is Episode ep && libraryOnly.Contains(ep.SeriesId) && YearOf(ep) is { } y)
            {
                if (!missingYearsPerSeries.TryGetValue(ep.SeriesId, out var years))
                {
                    years = new List<int>();
                    missingYearsPerSeries[ep.SeriesId] = years;
                }

                years.Add(y);
            }
        }

        var seriesEra = new Dictionary<Guid, (int Min, int Max)>();
        foreach (var (id, range) in ownedYearRange)
        {
            missingYearsPerSeries.TryGetValue(id, out var missingYears);
            seriesEra[id] = EpisodeEra.Expand(range, missingYears);
        }

        var missingPerSeries = new Dictionary<Guid, int>();
        foreach (var item in missing)
        {
            if (item is Episode ep && libraryOnly.Contains(ep.SeriesId) && !IsLikelyReboot(ep, seriesEra))
            {
                missingPerSeries.TryGetValue(ep.SeriesId, out var c);
                missingPerSeries[ep.SeriesId] = c + 1;
            }
        }

        var cap = context.Config.MaxMissingEpisodesPerShow <= 0 ? int.MaxValue : context.Config.MaxMissingEpisodesPerShow;
        var perSeriesCount = new Dictionary<Guid, int>();
        var rebootSeries = new HashSet<Guid>();
        var cappedSeries = new HashSet<Guid>();
        var seriesInfo = new Dictionary<Guid, (int? Year, string? Tmdb)>();

        foreach (var item in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is not Episode episode || !libraryOnly.Contains(episode.SeriesId))
            {
                continue;
            }

            var id = episode.SeriesId;
            if (IsLikelyReboot(episode, seriesEra))
            {
                if (rebootSeries.Add(id))
                {
                    var era = seriesEra[id];
                    _logger.LogInformation(
                        "Series content: {Series} has missing episodes airing outside its episode era ({Min}-{Max}); skipping them as a likely same-named reboot",
                        episode.SeriesName,
                        era.Min,
                        era.Max);
                }

                continue;
            }

            perSeriesCount.TryGetValue(id, out var count);
            if (count >= cap)
            {
                if (cappedSeries.Add(id))
                {
                    _logger.LogInformation("Series content: {Series} has more than {Cap} missing episodes; truncated", episode.SeriesName, cap);
                }

                continue;
            }

            perSeriesCount[id] = count + 1;

            if (!seriesInfo.TryGetValue(id, out var info))
            {
                var series = _libraryManager.GetItemById(id);
                info = (series?.ProductionYear, series is null ? null : series.ProviderIdOrNull(ProviderIds.Tmdb));
                seriesInfo[id] = info;
            }

            var ownedCount = ownedPerSeries.TryGetValue(id, out var oc) ? oc : 0;
            var totalCount = ownedCount + (missingPerSeries.TryGetValue(id, out var mc) ? mc : 0);
            gaps.Add(BuildGap(episode, info.Year, info.Tmdb, ownedCount, totalCount));
        }

        return gaps;
    }

    // A rich gap from a virtual episode the server already tracks: it carries the episode's own ids and links
    // to the item and its season.
    private static GapItem BuildGap(Episode episode, int? seriesYear, string? seriesTmdb, int ownedCount, int totalCount)
    {
        var season = episode.ParentIndexNumber;
        var number = episode.IndexNumber;
        string? code = null;
        if (season.HasValue && number.HasValue)
        {
            var end = episode.IndexNumberEnd;
            code = end.HasValue && end.Value > number.Value
                ? string.Create(CultureInfo.InvariantCulture, $"S{season.Value:D2}E{number.Value:D2}-E{end.Value:D2}")
                : string.Create(CultureInfo.InvariantCulture, $"S{season.Value:D2}E{number.Value:D2}");
        }

        var name = code is null
            ? string.Create(CultureInfo.InvariantCulture, $"{episode.SeriesName} - {episode.Name}")
            : string.Create(CultureInfo.InvariantCulture, $"{episode.SeriesName} {code} - {episode.Name}");

        var id = season.HasValue && number.HasValue
            ? SeriesGapKey.Episode(episode.SeriesId, season.Value, number.Value)
            : string.Create(CultureInfo.InvariantCulture, $"seriescontent:{episode.Id:N}");

        var gap = GapItemFactory.Create(
            id: id,
            pattern: GapPattern.SetCompletion,
            domain: MediaDomain.Shows,
            targetKind: BaseItemKind.Episode,
            name: name,
            providerIds: new Dictionary<string, string>(episode.ProviderIds, StringComparer.OrdinalIgnoreCase),
            sourceItemId: episode.SeriesId.ToString("N", CultureInfo.InvariantCulture),
            sourceItemName: episode.SeriesName,
            sourceItemType: "Series",
            sourceProviderIds: string.IsNullOrEmpty(seriesTmdb) ? null : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = seriesTmdb },
            releaseDate: episode.PremiereDate,
            overview: episode.Overview,
            season: season,
            sourceItemYear: seriesYear,
            setOwnedCount: ownedCount,
            setTotalCount: totalCount);

        gap.LibraryItemId = episode.Id.ToString("N", CultureInfo.InvariantCulture);
        if (episode.SeasonId != Guid.Empty)
        {
            gap.SeasonItemId = episode.SeasonId.ToString("N", CultureInfo.InvariantCulture);
        }

        gap.WatchTmdbId = seriesTmdb;
        return gap;
    }

    // A lean gap for an episode only a provider knows about (the server has no virtual item to link to).
    private static GapItem BuildLeanGap(BaseItem series, CanonicalEpisode episode, string? seriesTmdb, int ownedCount, int totalCount)
    {
        var code = string.Create(CultureInfo.InvariantCulture, $"S{episode.Season:D2}E{episode.Number:D2}");
        var name = string.IsNullOrEmpty(episode.Name)
            ? string.Create(CultureInfo.InvariantCulture, $"{series.Name} {code}")
            : string.Create(CultureInfo.InvariantCulture, $"{series.Name} {code} - {episode.Name}");

        var gap = GapItemFactory.Create(
            id: SeriesGapKey.Episode(series.Id, episode.Season, episode.Number),
            pattern: GapPattern.SetCompletion,
            domain: MediaDomain.Shows,
            targetKind: BaseItemKind.Episode,
            name: name,
            providerIds: new Dictionary<string, string>(),
            sourceItemId: series.Id.ToString("N", CultureInfo.InvariantCulture),
            sourceItemName: series.Name,
            sourceItemType: "Series",
            sourceProviderIds: string.IsNullOrEmpty(seriesTmdb) ? null : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = seriesTmdb },
            releaseDate: episode.ReleaseDate,
            overview: episode.Overview,
            season: episode.Season,
            sourceItemYear: series.ProductionYear,
            setOwnedCount: ownedCount,
            setTotalCount: totalCount);

        gap.WatchTmdbId = seriesTmdb;
        return gap;
    }

    private static bool IsLikelyReboot(Episode episode, IReadOnlyDictionary<Guid, (int Min, int Max)> seriesEra)
    {
        (int Min, int Max)? era = seriesEra.TryGetValue(episode.SeriesId, out var e) ? e : null;
        return EpisodeEra.IsOutside(YearOf(episode), era);
    }

    private static int? YearOf(Episode episode)
    {
        if (episode.PremiereDate is { } date && date.Year > 1900)
        {
            return date.Year;
        }

        return episode.ProductionYear is { } year && year > 1900 ? year : null;
    }
}
