namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Whether an owned movie or series is on the signed-in user's want-to-watch playlist, for its page's bookmark.
/// </summary>
public sealed class WantedItemState
{
    /// <summary>
    /// Gets or sets a value indicating whether the title is on the playlist.
    /// </summary>
    public bool OnList { get; set; }
}
