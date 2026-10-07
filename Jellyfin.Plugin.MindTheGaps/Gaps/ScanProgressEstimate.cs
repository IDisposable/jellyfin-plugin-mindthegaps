using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Turns the concurrent sources' own fractions into one figure for the scan. The sources run at the same time,
/// so the scan ends when the slowest one does: the honest figure is how much of the longest expected run is
/// left, not the average of the fractions, which reads mostly done once the fast sources finish and then
/// creeps for the rest of the scan. Each source's expected run is how long it took last time.
/// </summary>
internal static class ScanProgressEstimate
{
    // A source that finished almost at once still has a little weight, so a run of instant sources does not
    // divide by zero and a source last seen at zero seconds is not ignored if it turns slow.
    private const double MinSeconds = 1.0;

    /// <summary>
    /// Fills in the expected run of a source with no history: the median of the ones that have one, so a newly
    /// enabled source is neither ignored nor assumed to be the slowest.
    /// </summary>
    /// <param name="lastSeconds">Each source's last run in seconds, or null when it has none.</param>
    /// <returns>An expected run per source, or null when no source has a history to go on.</returns>
    public static double[]? Expected(IReadOnlyList<double?> lastSeconds)
    {
        ArgumentNullException.ThrowIfNull(lastSeconds);

        var known = lastSeconds.Where(s => s.HasValue).Select(s => Math.Max(MinSeconds, s!.Value)).OrderBy(s => s).ToArray();
        if (known.Length == 0)
        {
            return null;
        }

        var median = known.Length % 2 == 1
            ? known[known.Length / 2]
            : (known[(known.Length / 2) - 1] + known[known.Length / 2]) / 2;
        return lastSeconds.Select(s => s.HasValue ? Math.Max(MinSeconds, s.Value) : median).ToArray();
    }

    /// <summary>
    /// The scan's progress: one less the largest expected time a source still has to run, over the longest
    /// expected run. Never moves backwards while each source's own fraction only grows. With no history it is
    /// the plain average, since there is nothing to weigh by.
    /// </summary>
    /// <param name="fractions">Each source's own progress, 0 to 1.</param>
    /// <param name="expectedSeconds">Each source's expected run (from <see cref="Expected"/>), or null.</param>
    /// <returns>The scan's progress, 0 to 1.</returns>
    public static double Combine(IReadOnlyList<double> fractions, IReadOnlyList<double>? expectedSeconds)
    {
        ArgumentNullException.ThrowIfNull(fractions);
        if (fractions.Count == 0)
        {
            return 0;
        }

        if (expectedSeconds is null || expectedSeconds.Count != fractions.Count)
        {
            return fractions.Sum(f => Math.Clamp(f, 0, 1)) / fractions.Count;
        }

        double longest = 0;
        double remaining = 0;
        for (var i = 0; i < fractions.Count; i++)
        {
            var expected = Math.Max(MinSeconds, expectedSeconds[i]);
            longest = Math.Max(longest, expected);
            remaining = Math.Max(remaining, expected * (1 - Math.Clamp(fractions[i], 0, 1)));
        }

        return 1 - (remaining / longest);
    }
}
