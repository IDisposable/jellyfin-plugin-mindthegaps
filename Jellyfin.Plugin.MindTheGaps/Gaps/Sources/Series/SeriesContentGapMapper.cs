using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Builds a <see cref="GapItem"/> for one missing episode, and the two reboot-detection checks that guard
/// against reporting a same-named series' episodes as this one's. Pure, like every other source's mapper
/// (<see cref="Tmdb.CollectionGapMapper"/>, <see cref="Tmdb.FilmographyGapMapper"/>): no library or provider
/// call, so <see cref="SeriesContentGapSource"/> and <see cref="LibraryOnlySeriesGapFinder"/> both build
/// gaps and judge a reboot outlier the same way without either reaching outside what it was handed.
/// </summary>
internal static class SeriesContentGapMapper
{
    // The largest plausible gap between an owned series' start year and a provider's first season for it.
    // Beyond this the provider almost certainly resolved a same-named reboot, not the same series.
    private const int RebootYearGap = 3;

    /// <summary>
    /// True when the owned series has a year and the provider list's lowest season aired far enough from it
    /// to be a different, same-named series. Compares the lowest season's year to the start year, so it
    /// never rejects a legitimate long run (a later season airing decades on is fine).
    /// </summary>
    /// <param name="seriesYear">The owned series' production year, if known.</param>
    /// <param name="canonical">The provider's canonical episode list.</param>
    /// <returns>True when the list looks like a different, same-named series.</returns>
    public static bool LooksLikeDifferentSeries(int? seriesYear, IReadOnlyList<CanonicalEpisode> canonical)
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

    /// <summary>
    /// True when a library-only series' missing episode aired outside its expanded owned era, which is
    /// <see cref="LibraryOnlySeriesGapFinder"/>'s equivalent reboot check for a series no external provider
    /// can corroborate against.
    /// </summary>
    /// <param name="episode">The missing episode.</param>
    /// <param name="seriesEra">Each series' expanded owned episode era, by series id.</param>
    /// <returns>True when the episode looks like it belongs to a different, same-named series.</returns>
    public static bool IsLikelyReboot(Episode episode, IReadOnlyDictionary<Guid, (int Min, int Max)> seriesEra)
    {
        (int Min, int Max)? era = seriesEra.TryGetValue(episode.SeriesId, out var e) ? e : null;
        return EpisodeEra.IsOutside(YearOf(episode), era);
    }

    /// <summary>
    /// The year to place an episode by: its air year when it has one, else its production year, both only
    /// when plausible (not the epoch placeholder some sources stamp).
    /// </summary>
    /// <param name="episode">The episode.</param>
    /// <returns>The year, or null when neither is plausible.</returns>
    public static int? YearOf(Episode episode)
    {
        if (episode.PremiereDate is { } date && date.Year > 1900)
        {
            return date.Year;
        }

        return episode.ProductionYear is { } year && year > 1900 ? year : null;
    }

    /// <summary>
    /// A rich gap from a virtual episode the server already tracks: it carries the episode's own ids and
    /// links to the item and its season.
    /// </summary>
    /// <param name="episode">The server's own virtual episode item.</param>
    /// <param name="seriesYear">The owning series' production year, if known.</param>
    /// <param name="seriesTmdb">The owning series' TheMovieDb id, if known.</param>
    /// <param name="ownedCount">How many episodes of the series are owned.</param>
    /// <param name="totalCount">The total episode count (owned plus missing).</param>
    /// <returns>The gap.</returns>
    public static GapItem BuildGap(Episode episode, int? seriesYear, string? seriesTmdb, int ownedCount, int totalCount)
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
            : string.Create(CultureInfo.InvariantCulture, $"{GapSourceKeys.SeriesContent.GapPrefix}{episode.Id:N}");

        var gap = GapItemFactory.Create(
            id: id,
            pattern: GapPattern.SetCompletion,
            domain: MediaDomain.Shows,
            targetKind: BaseItemKind.Episode,
            name: name,
            providerIds: new Dictionary<string, string>(episode.ProviderIds, StringComparer.OrdinalIgnoreCase),
            sourceItemId: episode.SeriesId.ToString("N", CultureInfo.InvariantCulture),
            sourceItemName: episode.SeriesName,
            sourceItemType: SourceItemTypes.Series,
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

    /// <summary>
    /// A lean gap for an episode only a provider knows about (the server has no virtual item to link to).
    /// </summary>
    /// <param name="series">The owning series.</param>
    /// <param name="episode">The provider's canonical episode.</param>
    /// <param name="seriesTmdb">The owning series' TheMovieDb id, if known.</param>
    /// <param name="ownedCount">How many episodes of the series are owned.</param>
    /// <param name="totalCount">The total episode count (owned plus missing).</param>
    /// <returns>The gap.</returns>
    public static GapItem BuildLeanGap(BaseItem series, CanonicalEpisode episode, string? seriesTmdb, int ownedCount, int totalCount)
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
            sourceItemType: SourceItemTypes.Series,
            sourceProviderIds: string.IsNullOrEmpty(seriesTmdb) ? null : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = seriesTmdb },
            releaseDate: episode.ReleaseDate,
            overview: episode.Overview,
            season: episode.Season,
            sourceItemYear: series.ProductionYear,
            setOwnedCount: ownedCount,
            setTotalCount: totalCount,
            imageUrl: episode.ImageUrl);

        gap.WatchTmdbId = seriesTmdb;
        return gap;
    }
}
