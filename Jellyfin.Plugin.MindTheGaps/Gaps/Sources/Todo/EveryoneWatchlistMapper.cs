using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Todo;

/// <summary>
/// Builds "Everyone's watchlist" gaps from the fulfillment queue's own rows (<see cref="TodoDemandRow"/>,
/// from <see cref="TodoDemandAggregator"/>): the household's combined want list, treated as a discovery
/// source like any other watchlist, rather than only an admin tool.
/// </summary>
internal static class EveryoneWatchlistMapper
{
    // Only a title-level kind reads as a watchlist entry; an individual missing episode is set-completion
    // content (Shows already surfaces those under Set completion), not a discovery pick.
    private static readonly IReadOnlyDictionary<BaseItemKind, MediaDomain> Domains = new Dictionary<BaseItemKind, MediaDomain>
    {
        [BaseItemKind.Movie] = MediaDomain.Movies,
        [BaseItemKind.Series] = MediaDomain.Shows,
        [BaseItemKind.MusicAlbum] = MediaDomain.Music,
        [BaseItemKind.Book] = MediaDomain.Books
    };

    /// <summary>
    /// Builds a gap for every still-wanted row (<see cref="TodoDemandRow.OpenCount"/> greater than zero) the
    /// library does not already hold.
    /// </summary>
    /// <param name="rows">The fulfillment queue's rows.</param>
    /// <param name="ownership">The scan's ownership index.</param>
    /// <returns>The gaps.</returns>
    public static IEnumerable<GapItem> Build(IReadOnlyList<TodoDemandRow> rows, OwnershipIndex ownership)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(ownership);

        foreach (var row in rows)
        {
            if (row.OpenCount <= 0)
            {
                continue;
            }

            if (!Enum.TryParse<BaseItemKind>(row.TargetKindName, ignoreCase: false, out var kind)
                || !Domains.TryGetValue(kind, out var domain))
            {
                continue;
            }

            if (ownership.OwnsAny(kind, row.ProviderIds)
                || (kind == BaseItemKind.MusicAlbum && ownership.OwnsByName(kind, row.Creator, row.Name)))
            {
                continue;
            }

            yield return GapItemFactory.Create(
                id: GapSourceKeys.EveryoneWatchlist.Gap(row.Id),
                pattern: GapPattern.Recommendation,
                domain: domain,
                targetKind: kind,
                name: row.Name,
                providerIds: row.ProviderIds,
                sourceItemId: GapSourceKeys.EveryoneWatchlist.Owner(),
                sourceItemName: "Everyone's watchlist",
                sourceItemType: SourceItemTypes.EveryoneWatchlist,
                releaseDate: row.ReleaseDate,
                imageUrl: row.ImageUrl,
                overview: "Wanted by " + string.Join(", ", row.RequestedBy));
        }
    }
}
