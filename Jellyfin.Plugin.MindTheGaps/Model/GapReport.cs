using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The persisted result of a gap scan: the todo list.
/// </summary>
public class GapReport
{
    /// <summary>
    /// Gets or sets the UTC time the report was generated.
    /// </summary>
    public DateTime GeneratedUtc { get; set; }

    /// <summary>
    /// Gets or sets the plugin version that generated this report, so the dashboard can nudge for a
    /// rescan when a newer plugin would build it differently.
    /// </summary>
    public string GeneratedVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total number of gaps.
    /// </summary>
    public int TotalGaps { get; set; }

    /// <summary>
    /// Gets or sets the gap items.
    /// </summary>
    public IReadOnlyList<GapItem> Items { get; set; } = Array.Empty<GapItem>();
}
