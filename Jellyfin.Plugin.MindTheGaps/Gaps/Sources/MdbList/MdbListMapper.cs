using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.MdbList;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.MdbList;

/// <summary>
/// Turns the items of an MDBList list into discovery (<see cref="GapPattern.Recommendation"/>) gaps for the
/// titles the library does not own. Each item already carries its TMDB/IMDb/TheTVDB ids, so the gap keys on
/// those directly (no resolution step) and the ownership diff and link building work unchanged. The
/// <c>mediatype</c> routes a movie to the Movies domain and a show to the Shows domain.
/// </summary>
public static class MdbListMapper
{
    /// <summary>
    /// Builds gaps for a list's unowned items, de-duplicated by their strongest id and capped.
    /// </summary>
    /// <param name="listId">The MDBList list id.</param>
    /// <param name="listName">The list's display name (the gap's source).</param>
    /// <param name="items">The list's items.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="maxResults">The most gaps to emit for this list.</param>
    /// <returns>The discovery gaps for unowned items.</returns>
    public static IEnumerable<GapItem> Build(int listId, string? listName, IEnumerable<MdbListItem> items, OwnershipIndex ownership, int maxResults)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(ownership);

        var emitted = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (emitted >= maxResults)
            {
                break;
            }

            if (string.IsNullOrEmpty(item.Title))
            {
                continue;
            }

            var isShow = string.Equals(item.MediaType, "show", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.MediaType, "tv", StringComparison.OrdinalIgnoreCase);
            var kind = isShow ? BaseItemKind.Series : BaseItemKind.Movie;
            var domain = isShow ? MediaDomain.Shows : MediaDomain.Movies;

            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.TmdbId is > 0)
            {
                providerIds[ProviderIds.Tmdb] = item.TmdbId.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(item.ImdbId))
            {
                providerIds[ProviderIds.Imdb] = item.ImdbId;
            }

            if (isShow && item.TvdbId is > 0)
            {
                providerIds[ProviderIds.Tvdb] = item.TvdbId.Value.ToString(CultureInfo.InvariantCulture);
            }

            // Nothing to diff against or key on without at least one external id.
            var idKey = providerIds.TryGetValue(ProviderIds.Tmdb, out var tmdb) ? tmdb
                : providerIds.TryGetValue(ProviderIds.Imdb, out var imdb) ? imdb
                : null;
            if (idKey is null)
            {
                continue;
            }

            if (!seen.Add(idKey))
            {
                continue;
            }

            if (ownership.OwnsAny(kind, providerIds))
            {
                continue;
            }

            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"mdblist:{listId}:{idKey}"),
                pattern: GapPattern.Recommendation,
                domain: domain,
                targetKind: kind,
                name: item.Title,
                providerIds: providerIds,
                sourceItemId: string.Create(CultureInfo.InvariantCulture, $"mdblist-{listId}"),
                sourceItemName: listName,
                sourceItemType: "MdbList",
                releaseDate: item.ReleaseYear is > 0
                    ? new DateTime(item.ReleaseYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    : null);
        }
    }
}
