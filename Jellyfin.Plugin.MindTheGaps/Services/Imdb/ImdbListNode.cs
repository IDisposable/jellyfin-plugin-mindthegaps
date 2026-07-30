namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// A list entry, which wraps the thing listed.
/// </summary>
internal sealed class ImdbListNode
{
    /// <summary>
    /// Gets or sets the listed title or person.
    /// </summary>
    public ImdbListEntry? Item { get; set; }
}
