using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The report list the dashboard loads one tab at a time: <see cref="GapRow"/>s rather than full gaps.
/// </summary>
public class GapRowReport
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
    /// Gets or sets the total number of gaps in the report.
    /// </summary>
    public int TotalGaps { get; set; }

    /// <summary>
    /// Gets or sets the pattern every row shares, so it travels once instead of on each row. Null when the
    /// rows mix patterns, in which case each row carries its own.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PatternName { get; set; }

    /// <summary>
    /// Gets or sets the domain every row shares, hoisted the same way as <see cref="PatternName"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DomainName { get; set; }

    /// <summary>
    /// Gets or sets the distinct target kinds the rows point into by <see cref="GapRow.TargetKindRef"/>.
    /// </summary>
    public IReadOnlyList<string> TargetKinds { get; set; } = [];

    /// <summary>
    /// Gets or sets the rows.
    /// </summary>
    public IReadOnlyList<GapRow> Items { get; set; } = [];

    /// <summary>
    /// Gets or sets the distinct source-link lists the rows point into by <see cref="GapRow.SourceLinksRef"/>.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ExternalLink>> SourceLinkSets { get; set; } = [];

    /// <summary>
    /// Gets or sets what each discovery source did on the scan that produced this report.
    /// </summary>
    public IReadOnlyList<SourceRun> SourceRuns { get; set; } = [];
}
