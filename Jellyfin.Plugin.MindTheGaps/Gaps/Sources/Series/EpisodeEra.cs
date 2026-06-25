using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// The shared episode-era heuristic: the owned air-year run expanded through the missing-episode years into
/// the series' real episode era, and the test for whether a dated episode falls outside it. Both the library
/// scan (deciding which missing episodes to surface) and the diagnosis (judging one missing episode) use this
/// so they agree: an earlier or later season of a long run you only partly own bridges in episode by episode,
/// while a far-separated same-named reboot is left outside.
/// </summary>
internal static class EpisodeEra
{
    /// <summary>
    /// The widest gap between consecutive episode years (owned or missing) that still reads as one continuous
    /// run. A cluster separated from the series' episode era by more than this is a same-named reboot the
    /// series is mis-tagged as, not a real gap; an earlier or later season that bridges in within it stays.
    /// </summary>
    public const int RebootGapYears = 8;

    /// <summary>
    /// Expands the owned air-year run outward through the other (missing) episode years into the series' real
    /// episode era, absorbing any year within a reboot-sized gap of the current edge. A long run you only
    /// partly own stays one era because its earlier and later seasons bridge in episode by episode, so only a
    /// same-named reboot separated by a wide gap is left outside.
    /// </summary>
    /// <param name="owned">The min and max air year of the episodes you own for the series.</param>
    /// <param name="otherYears">The air years of the series' other (missing) episodes; may be null or empty.</param>
    /// <returns>The expanded era as a min and max year.</returns>
    public static (int Min, int Max) Expand((int Min, int Max) owned, IReadOnlyCollection<int>? otherYears)
    {
        var min = owned.Min;
        var max = owned.Max;
        if (otherYears is null || otherYears.Count == 0)
        {
            return (min, max);
        }

        while (true)
        {
            var below = int.MinValue;
            foreach (var year in otherYears)
            {
                if (year < min && year > below)
                {
                    below = year;
                }
            }

            if (below == int.MinValue || min - below > RebootGapYears)
            {
                break;
            }

            min = below;
        }

        while (true)
        {
            var above = int.MaxValue;
            foreach (var year in otherYears)
            {
                if (year > max && year < above)
                {
                    above = year;
                }
            }

            if (above == int.MaxValue || above - max > RebootGapYears)
            {
                break;
            }

            max = above;
        }

        return (min, max);
    }

    /// <summary>
    /// The pure outside-the-era test: true when a dated episode airs outside the series' episode era. The era
    /// already bridges reboot-sized gaps through the owned and missing episodes, so anything beyond it is a
    /// far-separated same-named reboot, not a real gap. False when the era or the episode year is unknown.
    /// </summary>
    /// <param name="episodeYear">The episode's air year, or null when it is undated.</param>
    /// <param name="era">The series' episode era, or null when it has no dated episodes to anchor against.</param>
    /// <returns>True when the episode airs outside the era.</returns>
    public static bool IsOutside(int? episodeYear, (int Min, int Max)? era)
        => era is { } range && episodeYear is { } year && (year < range.Min || year > range.Max);
}
