using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Gaps;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Leaves out of a surface's cards the titles the caller said they are not interested in.
/// </summary>
internal static class NotInterestedFilter
{
    /// <summary>
    /// The cards whose title is not among the caller's not-interested keys, in their order.
    /// </summary>
    /// <param name="titles">The cards.</param>
    /// <param name="notInterested">The identity keys of the titles the caller is not interested in
    /// (<see cref="NotInterestedStore.Keys"/>).</param>
    /// <returns>The cards to show.</returns>
    public static IReadOnlyList<MissingTitle> Without(IReadOnlyList<MissingTitle> titles, IReadOnlySet<string> notInterested)
    {
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(notInterested);

        return notInterested.Count == 0 ? titles : titles.Where(t => !notInterested.Contains(WantedMarker.KeyOf(t))).ToList();
    }
}
