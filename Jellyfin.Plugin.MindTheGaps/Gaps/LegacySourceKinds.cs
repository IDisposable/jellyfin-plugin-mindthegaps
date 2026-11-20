using System;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Retags an IMDb watchlist gap stored under <see cref="SourceItemTypes.ImdbList"/>, a kind that may show to
/// other users, as <see cref="SourceItemTypes.ImdbWatchlist"/>. IMDb names every watchlist "WATCHLIST", so
/// that name reads as a watchlist. Runs on every load, and does nothing once the kind is right.
/// </summary>
internal static class LegacySourceKinds
{
    private const string ImdbWatchlistName = "WATCHLIST";

    /// <summary>
    /// Retags the gap in place when it is an IMDb watchlist stored as a list.
    /// </summary>
    /// <param name="gap">The gap, just read.</param>
    public static void Recover(GapItem gap)
    {
        ArgumentNullException.ThrowIfNull(gap);
        if (string.Equals(gap.SourceItemType, SourceItemTypes.ImdbList, StringComparison.Ordinal)
            && string.Equals(gap.SourceItemName, ImdbWatchlistName, StringComparison.OrdinalIgnoreCase))
        {
            gap.SourceItemType = SourceItemTypes.ImdbWatchlist;
        }
    }
}
