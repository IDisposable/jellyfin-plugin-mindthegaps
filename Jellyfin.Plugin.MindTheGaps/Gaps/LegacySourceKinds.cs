using System;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Retags a stored gap whose kind an earlier version did not tell apart: an IMDb watchlist entered by its own
/// "ls" id was stored as <see cref="SourceItemTypes.ImdbList"/>, which may show to other users. IMDb names
/// every watchlist "WATCHLIST", so that name reads as a watchlist. Runs on every load, and does nothing once
/// the kind is right.
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
            && string.Equals(gap.SourceItemName, ImdbWatchlistName, StringComparison.Ordinal))
        {
            gap.SourceItemType = SourceItemTypes.ImdbWatchlist;
        }
    }
}
