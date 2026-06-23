using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Library;

/// <summary>
/// Surfaces missing series content into the todo list by reading the virtual (missing) episodes
/// that Jellyfin core already mints for owned series.
/// </summary>
public sealed class SeriesContentGapSource : IGapSource
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<SeriesContentGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesContentGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public SeriesContentGapSource(ILibraryManager libraryManager, ILogger<SeriesContentGapSource> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Series content";

    /// <inheritdoc />
    // Reads the library directly (missing episodes), so it needs nothing in the ownership index.
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = [];

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.ScanSeries;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Async iterator over a synchronous source; yield to keep the signature uniform.
        await Task.CompletedTask.ConfigureAwait(false);

        // One query for every missing episode in the library, then group/cap by series in memory.
        // This avoids a per-series query storm on large libraries.
        var missing = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            IsMissing = true,
            Recursive = true
        });

        // Owned episode counts and the owned air-year range per series, from the real (non-virtual)
        // episodes. The range seeds the episode era below, which expands it through the contiguous missing
        // episodes so an earlier or later season of a long run you only partly own stays listed.
        var ownedPerSeries = new Dictionary<Guid, int>();
        var ownedYearRange = new Dictionary<Guid, (int Min, int Max)>();
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            if (item is not Episode ep)
            {
                continue;
            }

            ownedPerSeries.TryGetValue(ep.SeriesId, out var c);
            ownedPerSeries[ep.SeriesId] = c + 1;

            var y = YearOf(ep);
            if (y.HasValue)
            {
                ownedYearRange[ep.SeriesId] = ownedYearRange.TryGetValue(ep.SeriesId, out var r)
                    ? (Math.Min(r.Min, y.Value), Math.Max(r.Max, y.Value))
                    : (y.Value, y.Value);
            }
        }

        // Expand each owned run through the series' missing-episode years into its real episode era. A long
        // running show you only partly own (you have 2008 to 2026 of a run that began in 1974) has its earlier
        // seasons as missing episodes contiguous with the owned run, so they are real gaps, not a reboot. Only
        // a missing cluster separated from the era by a reboot-sized gap (the "V 1984 tagged as V 2009" case)
        // is left outside and treated as a same-named reboot.
        var missingYearsPerSeries = new Dictionary<Guid, List<int>>();
        foreach (var item in missing)
        {
            if (item is Episode ep && YearOf(ep) is { } y)
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
        foreach (var (seriesId, owned) in ownedYearRange)
        {
            missingYearsPerSeries.TryGetValue(seriesId, out var missingYears);
            seriesEra[seriesId] = EpisodeEra.Expand(owned, missingYears);
        }

        // Per-series missing counts for the coverage badge ("59 of 62 owned"), excluding reboot episodes
        // (counting them would inflate the total against a run you do not actually have a gap in). The
        // per-show display cap below only truncates the listed rows, not this true total.
        var missingPerSeries = new Dictionary<Guid, int>();
        foreach (var item in missing)
        {
            if (item is Episode ep && !IsLikelyReboot(ep, seriesEra))
            {
                missingPerSeries.TryGetValue(ep.SeriesId, out var c);
                missingPerSeries[ep.SeriesId] = c + 1;
            }
        }

        // 0 in config means "no limit".
        var cap = context.Config.MaxMissingEpisodesPerShow;
        if (cap <= 0)
        {
            cap = int.MaxValue;
        }

        var perSeriesCount = new Dictionary<Guid, int>();
        var cappedSeries = new HashSet<Guid>();
        var rebootSeries = new HashSet<Guid>();
        var seriesInfo = new Dictionary<Guid, (int? Year, string? Tmdb)>();

        foreach (var item in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item is not Episode episode)
            {
                continue;
            }

            var seriesId = episode.SeriesId;

            // Skip missing episodes that fall outside the owned run by a reboot-sized gap: a same-named
            // reboot the owning series is mis-tagged as, not a real gap in the run you have (the "V 1984
            // tagged as V 2009" case). Logged once per series so the omission is visible.
            if (IsLikelyReboot(episode, seriesEra))
            {
                if (rebootSeries.Add(seriesId))
                {
                    var era = seriesEra[seriesId];
                    _logger.LogInformation(
                        "Series content: {Series} has missing episodes airing outside its episode era ({Min}-{Max}); skipping them as a likely same-named reboot mis-tagged onto this series",
                        episode.SeriesName,
                        era.Min,
                        era.Max);
                }

                continue;
            }

            perSeriesCount.TryGetValue(seriesId, out var count);
            if (count >= cap)
            {
                if (cappedSeries.Add(seriesId))
                {
                    _logger.LogInformation(
                        "Series content: {Series} has more than {Cap} missing episodes; truncated",
                        episode.SeriesName,
                        cap);
                }

                continue;
            }

            perSeriesCount[seriesId] = count + 1;

            // The owned series' year and TMDB id, resolved once per series (the episode carries neither).
            // The TMDB id lets the report look up where the show streams.
            if (!seriesInfo.TryGetValue(seriesId, out var info))
            {
                var series = _libraryManager.GetItemById(seriesId);
                var tmdb = series is not null && series.TryGetProviderId(MetadataProvider.Tmdb, out var t) && !string.IsNullOrEmpty(t) ? t : null;
                info = (series?.ProductionYear, tmdb);
                seriesInfo[seriesId] = info;
            }

            var ownedCount = ownedPerSeries.TryGetValue(seriesId, out var oc) ? oc : 0;
            var totalCount = ownedCount + (missingPerSeries.TryGetValue(seriesId, out var mc) ? mc : 0);
            yield return BuildGap(episode, info.Year, info.Tmdb, ownedCount, totalCount);
        }
    }

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

        var providerIds = new Dictionary<string, string>(episode.ProviderIds, StringComparer.OrdinalIgnoreCase);

        // Share the episode key with the TVmaze/TheTVDB cross-checks so the same missing episode
        // surfaced by more than one source collapses to a single todo entry.
        var id = season.HasValue && number.HasValue
            ? SeriesGapKey.Episode(episode.SeriesId, season.Value, number.Value)
            : string.Create(CultureInfo.InvariantCulture, $"seriescontent:{episode.Id:N}");

        var gap = GapItemFactory.Create(
            id: id,
            pattern: GapPattern.SetCompletion,
            domain: MediaDomain.Shows,
            targetKind: BaseItemKind.Episode,
            name: name,
            providerIds: providerIds,
            sourceItemId: episode.SeriesId.ToString("N", CultureInfo.InvariantCulture),
            sourceItemName: episode.SeriesName,
            sourceItemType: "Series",
            sourceProviderIds: string.IsNullOrEmpty(seriesTmdb) ? null : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [GapScanContext.TmdbProvider] = seriesTmdb },
            releaseDate: episode.PremiereDate,
            overview: episode.Overview,
            season: season,
            sourceItemYear: seriesYear,
            setOwnedCount: ownedCount,
            setTotalCount: totalCount);

        // This gap is a (virtual) episode the server already tracks, so the report can link to it and
        // its season directly.
        gap.LibraryItemId = episode.Id.ToString("N", CultureInfo.InvariantCulture);
        if (episode.SeasonId != Guid.Empty)
        {
            gap.SeasonItemId = episode.SeasonId.ToString("N", CultureInfo.InvariantCulture);
        }

        // Look up "where to watch" against the owning series (the episode has no streaming page of its own).
        gap.WatchTmdbId = seriesTmdb;

        return gap;
    }

    // True when a missing episode airs outside its series' episode era, so it reads as a same-named reboot
    // rather than a real gap. False when the series has no dated owned episodes to anchor against, or the
    // episode itself is undated (cannot tell).
    private static bool IsLikelyReboot(Episode episode, IReadOnlyDictionary<Guid, (int Min, int Max)> seriesEra)
    {
        (int Min, int Max)? era = seriesEra.TryGetValue(episode.SeriesId, out var e) ? e : null;
        return EpisodeEra.IsOutside(YearOf(episode), era);
    }

    // The episode's air year: the premiere (air) date when set, else the production year. A missing
    // episode core synthesizes can carry one but not the other, and the unset-date sentinel some items
    // hold is ignored. Owned and missing episodes both resolve their year this way, so the comparison is fair.
    private static int? YearOf(Episode episode)
    {
        if (episode.PremiereDate is { } date && date.Year > 1900)
        {
            return date.Year;
        }

        return episode.ProductionYear is { } year && year > 1900 ? year : null;
    }
}
