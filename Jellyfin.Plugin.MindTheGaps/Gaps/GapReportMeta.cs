using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// The report's meta file: what is not one domain's (the scan stamp, the source runs), and each streaming
/// service's logo, which its offers do not store (see <see cref="StoredAvailability"/>). The scan fields keep the
/// names a <see cref="GapReport"/> serializes them under, so a meta file written before the logos reads the same.
/// </summary>
internal sealed class GapReportMeta
{
    /// <summary>
    /// Gets or sets when the report was generated.
    /// </summary>
    public DateTime GeneratedUtc { get; set; }

    /// <summary>
    /// Gets or sets the plugin version that generated it.
    /// </summary>
    public string GeneratedVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the gap count.
    /// </summary>
    public int TotalGaps { get; set; }

    /// <summary>
    /// Gets or sets the last scan's source runs.
    /// </summary>
    public IReadOnlyList<SourceRun> SourceRuns { get; set; } = [];

    /// <summary>
    /// Gets or sets each streaming service's logo address, by service name.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Logos { get; set; }
}
