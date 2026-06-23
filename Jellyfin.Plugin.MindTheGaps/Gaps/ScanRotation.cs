using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// The stalest-first ordering shared by the rotating sources (people, recommendations, the series
/// cross-checks): a never-scanned candidate ranks oldest, so over repeated runs every candidate is scanned
/// and then the longest-unscanned refresh. One place for the ordering; each source adds its own tiebreak and
/// prunes and marks the cursor via <see cref="ScanCursorStore"/>.
/// </summary>
public static class ScanRotation
{
    /// <summary>
    /// Orders candidates stalest-first by their last-scanned time, a never-scanned candidate ranking oldest.
    /// </summary>
    /// <typeparam name="T">The candidate type.</typeparam>
    /// <param name="candidates">The candidates to order.</param>
    /// <param name="lastScanned">The per-key last-scanned times from <see cref="ScanCursorStore.GetLastScanned"/>.</param>
    /// <param name="key">Selects a candidate's rotation key.</param>
    /// <returns>The candidates ordered stalest-first; add a tiebreak with <c>ThenBy</c>.</returns>
    public static IOrderedEnumerable<T> OrderByStalest<T>(this IEnumerable<T> candidates, IReadOnlyDictionary<string, DateTime> lastScanned, Func<T, string> key)
        => candidates.OrderBy(c => lastScanned.TryGetValue(key(c), out var t) ? t : DateTime.MinValue);
}
