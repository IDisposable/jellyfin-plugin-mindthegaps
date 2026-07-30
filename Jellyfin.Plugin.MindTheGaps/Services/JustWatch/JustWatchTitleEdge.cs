namespace Jellyfin.Plugin.MindTheGaps.Services.JustWatch;

/// <summary>
/// A Relay edge over a list entry.
/// </summary>
internal sealed class JustWatchTitleEdge
{
    /// <summary>
    /// Gets or sets the entry.
    /// </summary>
    public JustWatchTitle? Node { get; set; }
}
