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
/// provider knows about is reported lean. Series no external provider can resolve are surfaced in bulk by
/// <see cref="LibraryOnlySeriesGapFinder"/> instead, so a large library's missing episodes appear every run
/// regardless of the per-run cap on the providers' (rate-limited) cross-checks; both paths build their gaps
/// through the shared, pure <see cref="SeriesContentGapMapper"/>.
/// </summary>
internal sealed class SeriesContentGapSource : IGapSource, ISeriesContentSource
{
    // The number of provider-resolvable series to cross-check (hit the providers' APIs for) in one run.
    private const int MaxSeries = 300;

    private readonly ILibraryManager _libraryManager;
    private readonly IReadOnlyList<ISeriesEpisodeProvider> _providers;
    private readonly ScanCursorStore _cursors;
    private readonly LibraryOnlySeriesGapFinder _libraryOnly;
    private readonly ILogger<SeriesContentGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesContentGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="providers">The episode providers to merge for each series.</param>
    /// <param name="cursors">Tracks which series were cross-checked, for stalest-first rotation.</param>
    /// <param name="libraryOnly">Surfaces the missing episodes of a series no provider can resolve.</param>
    /// <param name="logger">The logger.</param>
    public SeriesContentGapSource(
        ILibraryManager libraryManager,
        IEnumerable<ISeriesEpisodeProvider> providers,
        ScanCursorStore cursors,
        LibraryOnlySeriesGapFinder libraryOnly,
        ILogger<SeriesContentGapSource> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _libraryManager = libraryManager;
        _providers = providers.ToList();
        _cursors = cursors;
        _libraryOnly = libraryOnly;
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
            DtoOptions = LibraryQueryOptions.WithProviderIds(),
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
            if (_providers.Any(p => p.CanResolve(series, context.Config) && SeriesContentPriority.Uses(order, p.Provider)))
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
        foreach (var gap in _libraryOnly.FindGaps(libraryOnly, context, cancellationToken))
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
            .Where(p => !ServiceCircuit.IsOpen(p.ServiceName) && p.CanResolve(series, context.Config) && SeriesContentPriority.Uses(order, p.Provider))
            .OrderBy(p => SeriesContentPriority.Rank(order, p.Provider))
            .ToList();

        var lists = new List<IReadOnlyList<CanonicalEpisode>>();
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = await provider.GetCanonicalEpisodesAsync(series, context, cancellationToken).ConfigureAwait(false);

            // Guard against a provider resolving a same-named reboot (V 1984 versus V 2009): if its lowest
            // season aired far from the owned series' year, drop its list rather than report a reboot's seasons.
            if (list is { Count: > 0 } && !SeriesContentGapMapper.LooksLikeDifferentSeries(series.ProductionYear, list))
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
                ? SeriesContentGapMapper.BuildGap(item, series.ProductionYear, seriesTmdb, view.OwnedCount, totalCount)
                : SeriesContentGapMapper.BuildLeanGap(series, episode, seriesTmdb, view.OwnedCount, totalCount));
        }

        return gaps;
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
            DtoOptions = LibraryQueryOptions.Minimal(),
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

            if (SeriesContentGapMapper.YearOf(episode) is { } y)
            {
                ownedRange = ownedRange is { } r ? (Math.Min(r.Min, y), Math.Max(r.Max, y)) : (y, y);
            }
        }

        var virtuals = _libraryManager.GetItemList(new InternalItemsQuery
        {
            DtoOptions = LibraryQueryOptions.WithProviderIds(),
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
            var missingYears = virtuals.OfType<Episode>().Select(SeriesContentGapMapper.YearOf).OfType<int>().ToList();
            era = EpisodeEra.Expand(range, missingYears);
        }

        var lastChance = new List<CanonicalEpisode>();
        var byKey = new Dictionary<(int Season, int Number), Episode>();
        foreach (var item in virtuals)
        {
            if (item is not Episode episode
                || episode.ParentIndexNumber is not int season
                || episode.IndexNumber is not int number
                || EpisodeEra.IsOutside(SeriesContentGapMapper.YearOf(episode), era))
            {
                continue;
            }

            byKey[(season, number)] = episode;
            // No image here: this is the library's own virtual episode, and resolving one of its own
            // images would need Jellyfin's item-image URL scheme rather than a remote provider URL,
            // which is a different mechanism this source has no other reason to carry.
            lastChance.Add(new CanonicalEpisode(season, number, episode.Name, episode.PremiereDate, episode.Overview, null));
        }

        return (owned, ownedCount, lastChance, byKey);
    }
}
