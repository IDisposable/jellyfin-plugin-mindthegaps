using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Pure diff between an external source's canonical episode list and the episodes the library owns.
/// </summary>
public static class SeriesContentDiff
{
    /// <summary>
    /// Returns the canonical episodes that are not owned, skipping specials/unnumbered entries and
    /// de-duplicating by season/episode number, capped to <paramref name="cap"/>.
    /// </summary>
    /// <param name="canonical">The external source's episode list.</param>
    /// <param name="owned">The (season, number) pairs the library already has on disk.</param>
    /// <param name="cap">The maximum number of missing episodes to return.</param>
    /// <returns>The missing episodes, in source order.</returns>
    public static IReadOnlyList<CanonicalEpisode> Missing(
        IEnumerable<CanonicalEpisode> canonical,
        ISet<(int Season, int Number)> owned,
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
            if (owned.Contains(key) || !seen.Add(key))
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
