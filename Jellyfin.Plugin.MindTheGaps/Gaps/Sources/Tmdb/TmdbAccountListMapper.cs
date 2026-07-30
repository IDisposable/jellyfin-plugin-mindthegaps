using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using TMDbLib.Objects.Search;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Turns a TheMovieDb account's own lists (its watchlist and its favorites) into discovery
/// (<see cref="GapPattern.Recommendation"/>) gaps for the titles the library does not own. The entries are
/// ordinary TMDB search results, so they key on the TMDB id directly, and unlike the recommendation source
/// there is no vote floor: the user put these there on purpose, so an obscure one is still wanted.
/// </summary>
internal static class TmdbAccountListMapper
{
    /// <summary>
    /// Builds gaps for the unowned movies on an account list.
    /// </summary>
    /// <param name="results">The account's movies.</param>
    /// <param name="listKind">Which list this is, for the gap id and the source label.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Builds a poster URL from a TMDB poster path.</param>
    /// <param name="maxResults">The most gaps to emit.</param>
    /// <returns>The discovery gaps for unowned movies.</returns>
    public static IEnumerable<GapItem> BuildMovies(
        IEnumerable<SearchMovie> results,
        TmdbAccountListKind listKind,
        OwnershipIndex ownership,
        Func<string?, string?> posterUrl,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(posterUrl);

        var emitted = 0;
        var seen = new HashSet<int>();
        foreach (var movie in results)
        {
            if (emitted >= maxResults)
            {
                break;
            }

            if (string.IsNullOrEmpty(movie.Title) || !seen.Add(movie.Id))
            {
                continue;
            }

            var providerIds = TmdbId(movie.Id);
            if (ownership.OwnsAny(BaseItemKind.Movie, providerIds))
            {
                continue;
            }

            emitted++;
            yield return GapItemFactory.Create(
                id: GapId(listKind, movie.Id),
                pattern: GapPattern.Recommendation,
                domain: MediaDomain.Movies,
                targetKind: BaseItemKind.Movie,
                name: movie.Title,
                providerIds: providerIds,
                sourceItemId: OwnerId(listKind),
                sourceItemName: Label(listKind),
                sourceItemType: SourceItemTypes.TmdbAccountList,
                releaseDate: movie.ReleaseDate,
                imageUrl: posterUrl(movie.PosterPath),
                overview: movie.Overview,
                sortScore: movie.Popularity);
        }
    }

    /// <summary>
    /// Builds gaps for the unowned series on an account list.
    /// </summary>
    /// <param name="results">The account's series.</param>
    /// <param name="listKind">Which list this is, for the gap id and the source label.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Builds a poster URL from a TMDB poster path.</param>
    /// <param name="maxResults">The most gaps to emit.</param>
    /// <returns>The discovery gaps for unowned series.</returns>
    public static IEnumerable<GapItem> BuildSeries(
        IEnumerable<SearchTv> results,
        TmdbAccountListKind listKind,
        OwnershipIndex ownership,
        Func<string?, string?> posterUrl,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(posterUrl);

        var emitted = 0;
        var seen = new HashSet<int>();
        foreach (var series in results)
        {
            if (emitted >= maxResults)
            {
                break;
            }

            if (string.IsNullOrEmpty(series.Name) || !seen.Add(series.Id))
            {
                continue;
            }

            var providerIds = TmdbId(series.Id);
            if (ownership.OwnsAny(BaseItemKind.Series, providerIds))
            {
                continue;
            }

            emitted++;
            yield return GapItemFactory.Create(
                id: GapId(listKind, series.Id),
                pattern: GapPattern.Recommendation,
                domain: MediaDomain.Shows,
                targetKind: BaseItemKind.Series,
                name: series.Name,
                providerIds: providerIds,
                sourceItemId: OwnerId(listKind),
                sourceItemName: Label(listKind),
                sourceItemType: SourceItemTypes.TmdbAccountList,
                releaseDate: series.FirstAirDate,
                imageUrl: posterUrl(series.PosterPath),
                overview: series.Overview,
                sortScore: series.Popularity);
        }
    }

    /// <summary>
    /// Gets the display label for a list kind, which is the gap's source name on the report.
    /// </summary>
    /// <param name="listKind">The list kind.</param>
    /// <returns>The label.</returns>
    public static string Label(TmdbAccountListKind listKind)
        => listKind == TmdbAccountListKind.Favorites ? "TMDB favorites" : "TMDB watchlist";

    private static string GapId(TmdbAccountListKind listKind, int tmdbId)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{GapSourceKeys.TmdbAccountList.GapPrefix}{Token(listKind)}:{tmdbId}");

    private static string OwnerId(TmdbAccountListKind listKind)
        => GapSourceKeys.TmdbAccountList.Owner(Token(listKind));

    private static string Token(TmdbAccountListKind listKind)
        => listKind == TmdbAccountListKind.Favorites ? "favorites" : "watchlist";

    private static Dictionary<string, string> TmdbId(int tmdbId)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderIds.Tmdb] = tmdbId.ToString(CultureInfo.InvariantCulture)
        };
}
