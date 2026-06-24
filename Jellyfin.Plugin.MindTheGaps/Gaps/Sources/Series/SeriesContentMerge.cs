using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Merges several providers' canonical episode lists for one series, in priority order, by season: each
/// season is claimed by the highest-priority provider that has any episodes in it, and lower providers only
/// contribute seasons no higher provider opined on. So a secondary provider can add a whole season the
/// primary does not list, but cannot add episodes to a season the primary already covers (the primary's
/// episode set for a season it covers is final). Specials (season 0) are dropped, as the diff also skips them.
/// </summary>
public static class SeriesContentMerge
{
    /// <summary>
    /// Combines the given canonical lists, highest priority first, claiming each season for the first list
    /// that has it.
    /// </summary>
    /// <param name="providerLists">Each provider's canonical episodes, highest priority first.</param>
    /// <returns>The combined canonical episode list.</returns>
    public static IReadOnlyList<CanonicalEpisode> Combine(IReadOnlyList<IReadOnlyList<CanonicalEpisode>> providerLists)
    {
        ArgumentNullException.ThrowIfNull(providerLists);

        var claimedSeasons = new HashSet<int>();
        var combined = new List<CanonicalEpisode>();

        foreach (var list in providerLists)
        {
            if (list is null || list.Count == 0)
            {
                continue;
            }

            // The seasons this provider covers that no higher-priority provider already claimed.
            var newSeasons = new HashSet<int>();
            foreach (var episode in list)
            {
                if (episode.Season >= 1 && !claimedSeasons.Contains(episode.Season))
                {
                    newSeasons.Add(episode.Season);
                }
            }

            if (newSeasons.Count == 0)
            {
                continue;
            }

            foreach (var episode in list)
            {
                if (newSeasons.Contains(episode.Season))
                {
                    combined.Add(episode);
                }
            }

            claimedSeasons.UnionWith(newSeasons);
        }

        return combined;
    }
}
