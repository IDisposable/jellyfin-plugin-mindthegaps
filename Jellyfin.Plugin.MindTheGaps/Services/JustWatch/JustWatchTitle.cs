namespace Jellyfin.Plugin.MindTheGaps.Services.JustWatch;

/// <summary>
/// A title on a JustWatch list.
/// </summary>
internal sealed class JustWatchTitle
{
    /// <summary>
    /// Gets or sets the node id ("tm10", "ts4").
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the kind, "MOVIE" or "SHOW".
    /// </summary>
    public string? ObjectType { get; set; }

    /// <summary>
    /// Gets or sets the localized detail.
    /// </summary>
    public JustWatchContent? Content { get; set; }
}
