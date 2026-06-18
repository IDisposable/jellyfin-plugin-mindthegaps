using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using TMDbLib.Objects.Search;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Turns a TMDB discover result for a curated set (a studio/company or a keyword) into SetCompletion
/// gaps for the movies in that set the library does not own. Widens SetCompletion beyond formal TMDB
/// BoxSets to broader sets like "every A24 film" or "every Studio Ghibli film".
/// </summary>
public static class CuratedSetGapMapper
{
    /// <summary>
    /// Builds movie gaps for the unowned members of a curated set.
    /// </summary>
    /// <param name="results">The discover results for the set.</param>
    /// <param name="setKey">A stable key for the set, for example "company:41077" or "keyword:9715".</param>
    /// <param name="setLabel">A human label for the set, for example "A24".</param>
    /// <param name="setType">The set type label, for example "Studio" or "Keyword".</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <param name="perSet">The maximum gaps to emit for this set.</param>
    /// <returns>The set-completion gaps.</returns>
    public static IEnumerable<GapItem> BuildMovies(
        IEnumerable<SearchMovie> results,
        string setKey,
        string setLabel,
        string setType,
        OwnershipIndex ownership,
        Func<string?, string?> posterUrl,
        int perSet)
    {
        var emitted = 0;
        foreach (var movie in results)
        {
            if (emitted >= perSet)
            {
                break;
            }

            if (string.IsNullOrEmpty(movie.Title))
            {
                continue;
            }

            var providerIds = TmdbId(movie.Id);
            if (ownership.OwnsAny(BaseItemKind.Movie, providerIds))
            {
                continue;
            }

            var year = movie.ReleaseDate?.Year;
            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"curated:{setKey}:{movie.Id}"),
                pattern: GapPattern.SetCompletion,
                domain: MediaDomain.Movies,
                targetKind: BaseItemKind.Movie,
                name: movie.Title,
                providerIds: providerIds,
                sourceItemId: string.Empty,
                sourceItemName: setLabel,
                sourceItemType: setType,
                releaseDate: movie.ReleaseDate,
                imageUrl: posterUrl(movie.PosterPath),
                overview: movie.Overview,
                sourceItemYear: year);
        }
    }

    private static Dictionary<string, string> TmdbId(int id)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            [GapScanContext.TmdbProvider] = id.ToString(CultureInfo.InvariantCulture)
        };
}
