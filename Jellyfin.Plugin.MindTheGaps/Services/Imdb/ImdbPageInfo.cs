namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// Cursor pagination state for a list's items.
/// </summary>
internal sealed class ImdbPageInfo
{
    /// <summary>
    /// Gets or sets a value indicating whether another page follows.
    /// </summary>
    public bool HasNextPage { get; set; }

    /// <summary>
    /// Gets or sets the cursor to pass as the next request's <c>after</c>. IMDb reports
    /// <see cref="HasNextPage"/> true with a null cursor on an empty page, so the paging loop stops on
    /// either, not on the flag alone.
    /// </summary>
    public string? EndCursor { get; set; }
}
