using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Surfaces the missing episodes of a series no external provider can cross-check (no fetcher configured,
/// no credentials, or no id it resolves by), from the library's own virtual episodes alone. Split out of
/// <see cref="SeriesContentGapSource"/> because it is a complete, self-contained pass: one bulk read
/// covering every library-only series at once, rather than the provider-resolvable path's per-series merge,
/// so a large library's missing episodes still appear every run regardless of how the provider batch is
/// capped.
/// </summary>
internal sealed class LibraryOnlySeriesGapFinder
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryOnlySeriesGapFinder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryOnlySeriesGapFinder"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public LibraryOnlySeriesGapFinder(ILibraryManager libraryManager, ILogger<LibraryOnlySeriesGapFinder> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Finds the missing-episode gaps for every given library-only series, reboot outliers excluded and
    /// capped per show. Mirrors the provider-resolvable path's gaps so a series that later gains a provider
    /// id reports the same ids.
    /// </summary>
    /// <param name="libraryOnly">The series ids no external provider can resolve.</param>
    /// <param name="context">The scan context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The gaps.</returns>
    public List<GapItem> FindGaps(HashSet<Guid> libraryOnly, GapScanContext context, CancellationToken cancellationToken)
    {
        var gaps = new List<GapItem>();
        if (libraryOnly.Count == 0)
        {
            return gaps;
        }

        var missing = _libraryManager.GetItemList(new InternalItemsQuery
        {
            DtoOptions = LibraryQueryOptions.WithProviderIds(),
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            IsMissing = true,
            Recursive = true
        });

        // Owned counts and air-year range per series, from the real episodes, to seed each series' era.
        var ownedPerSeries = new Dictionary<Guid, int>();
        var ownedYearRange = new Dictionary<Guid, (int Min, int Max)>();
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            DtoOptions = LibraryQueryOptions.Minimal(),
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

            if (SeriesContentGapMapper.YearOf(ep) is { } y)
            {
                ownedYearRange[ep.SeriesId] = ownedYearRange.TryGetValue(ep.SeriesId, out var r)
                    ? (Math.Min(r.Min, y), Math.Max(r.Max, y))
                    : (y, y);
            }
        }

        var missingYearsPerSeries = new Dictionary<Guid, List<int>>();
        foreach (var item in missing)
        {
            if (item is Episode ep && libraryOnly.Contains(ep.SeriesId) && SeriesContentGapMapper.YearOf(ep) is { } y)
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
            if (item is Episode ep && libraryOnly.Contains(ep.SeriesId) && !SeriesContentGapMapper.IsLikelyReboot(ep, seriesEra))
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
            if (SeriesContentGapMapper.IsLikelyReboot(episode, seriesEra))
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
            gaps.Add(SeriesContentGapMapper.BuildGap(episode, info.Year, info.Tmdb, ownedCount, totalCount));
        }

        return gaps;
    }
}
