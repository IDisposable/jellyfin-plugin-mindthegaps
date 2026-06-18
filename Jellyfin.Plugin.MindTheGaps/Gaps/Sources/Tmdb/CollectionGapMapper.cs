using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using TMDbLib.Objects.Search;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Turns a TMDB collection's parts into movie gaps for the parts the library doesn't own.
/// </summary>
public static class CollectionGapMapper
{
    /// <summary>
    /// Builds a gap for every collection part not present in <paramref name="ownership"/>.
    /// </summary>
    /// <param name="collectionId">The TMDB collection id (used in the gap id).</param>
    /// <param name="parts">The collection's parts (TMDB models these as movies).</param>
    /// <param name="boxSetId">The owned BoxSet's id.</param>
    /// <param name="boxSetName">The owned BoxSet's name.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <returns>The missing-movie gaps.</returns>
    public static IEnumerable<GapItem> Build(
        int collectionId,
        IEnumerable<SearchMovie> parts,
        string boxSetId,
        string? boxSetName,
        OwnershipIndex ownership,
        Func<string?, string?> posterUrl)
    {
        // Materialize so the set's owned/total counts can be computed for coverage scoring.
        var partList = new List<SearchMovie>(parts);
        var total = partList.Count;
        var owned = 0;
        var missing = new List<(SearchMovie Part, Dictionary<string, string> Ids)>();
        foreach (var part in partList)
        {
            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [GapScanContext.TmdbProvider] = part.Id.ToString(CultureInfo.InvariantCulture)
            };

            if (ownership.OwnsAny(BaseItemKind.Movie, providerIds))
            {
                owned++;
                continue;
            }

            missing.Add((part, providerIds));
        }

        foreach (var (part, providerIds) in missing)
        {
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"collection:{collectionId}:{part.Id}"),
                pattern: GapPattern.SetCompletion,
                domain: MediaDomain.Movies,
                targetKind: BaseItemKind.Movie,
                name: part.Title ?? string.Empty,
                providerIds: providerIds,
                sourceItemId: boxSetId,
                sourceItemName: boxSetName,
                sourceItemType: "BoxSet",
                releaseDate: part.ReleaseDate,
                imageUrl: posterUrl(part.PosterPath),
                overview: part.Overview,
                setOwnedCount: owned,
                setTotalCount: total);
        }
    }
}
