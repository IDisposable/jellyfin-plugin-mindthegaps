namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The outcome of toggling a title on the want-to-watch list.
/// </summary>
public sealed class WantToWatchState
{
    /// <summary>
    /// Gets or sets the gap id.
    /// </summary>
    public string GapId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the title is now on the list.
    /// </summary>
    public bool Wanted { get; set; }
}
