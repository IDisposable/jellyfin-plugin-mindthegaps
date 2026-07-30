namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// A Relay edge over a list entry.
/// </summary>
internal sealed class ImdbListEdge
{
    /// <summary>
    /// Gets or sets the entry.
    /// </summary>
    public ImdbListNode? Node { get; set; }
}
