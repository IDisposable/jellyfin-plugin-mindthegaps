using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Trakt;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Trakt;

/// <summary>
/// Turns the entries of a Trakt list into discovery (<see cref="GapPattern.Recommendation"/>) gaps for the
/// titles the library does not own. Each entry already carries its TMDB/IMDb ids, so the gap keys on those
/// directly (no resolution step) and the ownership diff and link building work unchanged. The entry type
/// routes a movie to the Movies domain and a show to the Shows domain.
/// </summary>
internal static class TraktListMapper
{
    /// <summary>
    /// Builds gaps for a list's unowned entries, de-duplicated by their strongest id and capped.
    /// </summary>
    /// <param name="listId">The Trakt list id.</param>
    /// <param name="listName">The list's display name (the gap's source).</param>
    /// <param name="items">The list's entries.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="maxResults">The most gaps to emit for this list.</param>
    /// <returns>The discovery gaps for unowned entries.</returns>
    public static IEnumerable<GapItem> Build(long listId, string? listName, IEnumerable<TraktListItem> items, OwnershipIndex ownership, int maxResults)
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

            var isShow = string.Equals(item.Type, "show", StringComparison.OrdinalIgnoreCase);
            var kind = isShow ? BaseItemKind.Series : BaseItemKind.Movie;
            var domain = isShow ? MediaDomain.Shows : MediaDomain.Movies;

            var title = isShow ? item.Show?.Title : item.Movie?.Title;
            var year = isShow ? item.Show?.Year : item.Movie?.Year;
            var ids = isShow ? item.Show?.Ids : item.Movie?.Ids;
            if (string.IsNullOrEmpty(title) || ids is null)
            {
                continue;
            }

            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (ids.Tmdb is > 0)
            {
                providerIds[ProviderIds.Tmdb] = ids.Tmdb.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(ids.Imdb))
            {
                providerIds[ProviderIds.Imdb] = ids.Imdb;
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
                id: string.Create(CultureInfo.InvariantCulture, $"traktlist:{listId}:{idKey}"),
                pattern: GapPattern.Recommendation,
                domain: domain,
                targetKind: kind,
                name: title,
                providerIds: providerIds,
                sourceItemId: string.Create(CultureInfo.InvariantCulture, $"traktlist-{listId}"),
                sourceItemName: listName,
                sourceItemType: "TraktList",
                releaseDate: year is > 0
                    ? new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    : null);
        }
    }
}
