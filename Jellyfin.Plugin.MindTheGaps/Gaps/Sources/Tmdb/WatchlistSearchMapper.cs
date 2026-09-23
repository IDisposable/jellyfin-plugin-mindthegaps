using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Turns a TMDB title into a want-to-watch gap for the want-to-watch title search: a user picking a title
/// no page already listed, rather than a recommendation from an owned one. Carries no seed
/// (<see cref="GapItem.SourceItemId"/> is empty), and its own <see cref="GapSourceKey"/>
/// (<see cref="GapSourceKeys.WatchlistSearchMovie"/>/<see cref="GapSourceKeys.WatchlistSearchSeries"/>) so a
/// persisted todo entry added this way is not mistaken for one the scan itself recommended.
/// </summary>
internal static class WatchlistSearchMapper
{
    /// <summary>
    /// Builds cards for a movie search's results, unowned only.
    /// </summary>
    /// <param name="results">The search results.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <param name="limit">The most results to return.</param>
    /// <returns>The gaps.</returns>
    public static IEnumerable<GapItem> SearchMovies(IEnumerable<SearchMovie> results, OwnershipIndex ownership, Func<string?, string?> posterUrl, int limit)
        => RecommendationGapMapper.BuildMovies(results, string.Empty, null, null, ownership, posterUrl, limit, minVotes: 0, gapPrefix: GapSourceKeys.WatchlistSearchMovie.GapPrefix);

    /// <summary>
    /// Builds cards for a series search's results, unowned only.
    /// </summary>
    /// <param name="results">The search results.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <param name="limit">The most results to return.</param>
    /// <returns>The gaps.</returns>
    public static IEnumerable<GapItem> SearchSeries(IEnumerable<SearchTv> results, OwnershipIndex ownership, Func<string?, string?> posterUrl, int limit)
        => RecommendationGapMapper.BuildSeries(results, string.Empty, null, null, ownership, posterUrl, limit, minVotes: 0, gapPrefix: GapSourceKeys.WatchlistSearchSeries.GapPrefix);

    /// <summary>
    /// Builds the gap a chosen movie is added to the want-to-watch list under, rehydrated from its own TMDB
    /// detail record (not a fresh search) so the id being added always matches what the user picked.
    /// </summary>
    /// <param name="movie">The movie's TMDB detail record.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <returns>The gap, or <see langword="null"/> when the library already owns it.</returns>
    public static GapItem? FromMovieDetails(Movie movie, OwnershipIndex ownership, Func<string?, string?> posterUrl)
    {
        ArgumentNullException.ThrowIfNull(movie);

        if (string.IsNullOrEmpty(movie.Title))
        {
            return null;
        }

        var providerIds = TmdbId(movie.Id);
        if (ownership.OwnsAny(BaseItemKind.Movie, providerIds))
        {
            return null;
        }

        return GapItemFactory.Create(
            id: GapSourceKeys.WatchlistSearchMovie.Gap(movie.Id.ToString(CultureInfo.InvariantCulture)),
            pattern: GapPattern.Recommendation,
            domain: MediaDomain.Movies,
            targetKind: BaseItemKind.Movie,
            name: movie.Title,
            providerIds: providerIds,
            sourceItemId: string.Empty,
            sourceItemName: null,
            sourceItemType: string.Empty,
            releaseDate: movie.ReleaseDate,
            imageUrl: posterUrl(movie.PosterPath),
            overview: movie.Overview);
    }

    /// <summary>
    /// Builds the gap a chosen series is added to the want-to-watch list under, rehydrated from its own TMDB
    /// detail record (not a fresh search) so the id being added always matches what the user picked.
    /// </summary>
    /// <param name="show">The series' TMDB detail record.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <returns>The gap, or <see langword="null"/> when the library already owns it.</returns>
    public static GapItem? FromSeriesDetails(TvShow show, OwnershipIndex ownership, Func<string?, string?> posterUrl)
    {
        ArgumentNullException.ThrowIfNull(show);

        if (string.IsNullOrEmpty(show.Name))
        {
            return null;
        }

        var providerIds = TmdbId(show.Id);
        if (ownership.OwnsAny(BaseItemKind.Series, providerIds))
        {
            return null;
        }

        return GapItemFactory.Create(
            id: GapSourceKeys.WatchlistSearchSeries.Gap(show.Id.ToString(CultureInfo.InvariantCulture)),
            pattern: GapPattern.Recommendation,
            domain: MediaDomain.Shows,
            targetKind: BaseItemKind.Series,
            name: show.Name,
            providerIds: providerIds,
            sourceItemId: string.Empty,
            sourceItemName: null,
            sourceItemType: string.Empty,
            releaseDate: show.FirstAirDate,
            imageUrl: posterUrl(show.PosterPath),
            overview: show.Overview);
    }

    private static Dictionary<string, string> TmdbId(int id)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderIds.Tmdb] = id.ToString(CultureInfo.InvariantCulture)
        };
}
