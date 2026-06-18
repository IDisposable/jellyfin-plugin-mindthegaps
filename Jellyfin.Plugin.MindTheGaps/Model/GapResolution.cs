using System;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A note marking a gap as resolved (deliberately not "missing", for example two listed episodes that
/// are a single combined file). Persisted across scans by <see cref="GapItem.Id"/>.
/// </summary>
public class GapResolution
{
    /// <summary>
    /// Gets or sets the note explaining why the gap is not really missing.
    /// </summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC time the gap was resolved.
    /// </summary>
    public DateTime ResolvedUtc { get; set; }
}
