using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A lightweight overview of the report: the per-pattern gap counts and the streaming providers seen,
/// without the gap items themselves. The dashboard loads this to render the pattern tabs and seed the
/// provider filter, then fetches one pattern's items at a time, so a large report is not shipped whole.
/// </summary>
public class GapSummary
{
    /// <summary>
    /// Gets or sets the UTC time the report was generated.
    /// </summary>
    public DateTime GeneratedUtc { get; set; }

    /// <summary>
    /// Gets or sets the plugin version that generated the report.
    /// </summary>
    public string GeneratedVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total number of gaps across every pattern.
    /// </summary>
    public int TotalGaps { get; set; }

    /// <summary>
    /// Gets or sets the gap count per pattern, keyed by the pattern name (matching
    /// <see cref="GapItem.PatternName"/>), so the dashboard can label and order the tabs.
    /// </summary>
    public IReadOnlyDictionary<string, int> PatternCounts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the distinct streaming-provider names present anywhere in the report's availability,
    /// so the provider filter is fully populated before the per-pattern items load.
    /// </summary>
    public IReadOnlyList<string> Providers { get; set; } = Array.Empty<string>();
}
