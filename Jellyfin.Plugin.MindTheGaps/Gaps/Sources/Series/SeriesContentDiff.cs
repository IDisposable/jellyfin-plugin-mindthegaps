using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Pure diff between an external source's canonical episode list and the episodes the library owns.
/// </summary>
public static class SeriesContentDiff
{
    /// <summary>
    /// Returns the canonical episodes that are not owned, skipping specials/unnumbered entries and
    /// de-duplicating by season/episode number, capped to <paramref name="cap"/>. An episode the library
    /// owns under a different number (the source renumbers, reorders, or splits a two-part episode the
    /// library merged) is reconciled as owned via <see cref="OwnedEpisodes.Owns"/>, not reported missing.
    /// </summary>
    /// <param name="canonical">The external source's episode list.</param>
    /// <param name="owned">The episodes the library already has on disk.</param>
    /// <param name="cap">The maximum number of missing episodes to return.</param>
    /// <returns>The missing episodes, in source order.</returns>
    public static IReadOnlyList<CanonicalEpisode> Missing(
        IEnumerable<CanonicalEpisode> canonical,
        OwnedEpisodes owned,
        int cap)
    {
        var missing = new List<CanonicalEpisode>();
        var seen = new HashSet<(int Season, int Number)>();

        foreach (var episode in canonical)
        {
            if (episode.Season < 1 || episode.Number < 1)
            {
                continue;
            }

            var key = (episode.Season, episode.Number);
            if (owned.Owns(episode) || !seen.Add(key))
            {
                continue;
            }

            missing.Add(episode);
            if (missing.Count >= cap)
            {
                break;
            }
        }

        return missing;
    }
}
