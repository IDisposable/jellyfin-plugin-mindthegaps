using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using TMDbLib.Objects.Search;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Turns TMDB "similar" results for an owned title into discovery gaps for the unowned ones.
/// </summary>
public static class RecommendationGapMapper
{
    /// <summary>
    /// Builds movie recommendation gaps from a seed movie's similar results.
    /// </summary>
    /// <param name="results">The similar-movie results.</param>
    /// <param name="sourceItemId">The owned seed movie's id.</param>
    /// <param name="sourceItemName">The owned seed movie's name.</param>
    /// <param name="sourceItemYear">The owned seed movie's release year, if known.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <param name="perItem">The maximum gaps to emit for this seed.</param>
    /// <returns>The recommendation gaps.</returns>
    public static IEnumerable<GapItem> BuildMovies(
        IEnumerable<SearchMovie> results,
        string sourceItemId,
        string? sourceItemName,
        int? sourceItemYear,
        OwnershipIndex ownership,
        Func<string?, string?> posterUrl,
        int perItem)
    {
        var emitted = 0;
        foreach (var rec in results)
        {
            if (emitted >= perItem)
            {
                break;
            }

            if (string.IsNullOrEmpty(rec.Title))
            {
                continue;
            }

            var providerIds = TmdbId(rec.Id);
            if (ownership.OwnsAny(BaseItemKind.Movie, providerIds))
            {
                continue;
            }

            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"recommendation:movie:{rec.Id}"),
                pattern: GapPattern.Recommendation,
                domain: MediaDomain.Movies,
                targetKind: BaseItemKind.Movie,
                name: rec.Title,
                providerIds: providerIds,
                sourceItemId: sourceItemId,
                sourceItemName: sourceItemName,
                sourceItemType: "Movie",
                releaseDate: rec.ReleaseDate,
                imageUrl: posterUrl(rec.PosterPath),
                overview: rec.Overview,
                sourceItemYear: sourceItemYear,
                sortScore: rec.Popularity);
        }
    }

    /// <summary>
    /// Builds series recommendation gaps from a seed series' similar results.
    /// </summary>
    /// <param name="results">The similar-series results.</param>
    /// <param name="sourceItemId">The owned seed series' id.</param>
    /// <param name="sourceItemName">The owned seed series' name.</param>
    /// <param name="sourceItemYear">The owned seed series' release year, if known.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <param name="perItem">The maximum gaps to emit for this seed.</param>
    /// <returns>The recommendation gaps.</returns>
    public static IEnumerable<GapItem> BuildSeries(
        IEnumerable<SearchTv> results,
        string sourceItemId,
        string? sourceItemName,
        int? sourceItemYear,
        OwnershipIndex ownership,
        Func<string?, string?> posterUrl,
        int perItem)
    {
        var emitted = 0;
        foreach (var rec in results)
        {
            if (emitted >= perItem)
            {
                break;
            }

            if (string.IsNullOrEmpty(rec.Name))
            {
                continue;
            }

            var providerIds = TmdbId(rec.Id);
            if (ownership.OwnsAny(BaseItemKind.Series, providerIds))
            {
                continue;
            }

            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"recommendation:series:{rec.Id}"),
                pattern: GapPattern.Recommendation,
                domain: MediaDomain.Shows,
                targetKind: BaseItemKind.Series,
                name: rec.Name,
                providerIds: providerIds,
                sourceItemId: sourceItemId,
                sourceItemName: sourceItemName,
                sourceItemType: "Series",
                releaseDate: rec.FirstAirDate,
                imageUrl: posterUrl(rec.PosterPath),
                overview: rec.Overview,
                sourceItemYear: sourceItemYear,
                sortScore: rec.Popularity);
        }
    }

    private static Dictionary<string, string> TmdbId(int id)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            [GapScanContext.TmdbProvider] = id.ToString(CultureInfo.InvariantCulture)
        };
}
