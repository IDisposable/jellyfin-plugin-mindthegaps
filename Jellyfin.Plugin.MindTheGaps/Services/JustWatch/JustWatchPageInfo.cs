namespace Jellyfin.Plugin.MindTheGaps.Services.JustWatch;

/// <summary>
/// Cursor pagination state for a list.
/// </summary>
internal sealed class JustWatchPageInfo
{
    /// <summary>
    /// Gets or sets a value indicating whether another page follows.
    /// </summary>
    public bool HasNextPage { get; set; }

    /// <summary>
    /// Gets or sets the cursor to pass as the next request's <c>after</c>.
    /// </summary>
    public string? EndCursor { get; set; }
}
