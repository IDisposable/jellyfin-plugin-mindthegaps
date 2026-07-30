namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// An IMDb list. A user's watchlist is one of these too: the watchlist query returns the same type, with its
/// own "ls" id, which is why one type serves both.
/// </summary>
internal sealed class ImdbList
{
    /// <summary>
    /// Gets or sets the list id ("ls055576446").
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the list's display name.
    /// </summary>
    public ImdbText? Name { get; set; }

    /// <summary>
    /// Gets or sets what the list holds.
    /// </summary>
    public ImdbListType? ListType { get; set; }

    /// <summary>
    /// Gets or sets the requested page of entries.
    /// </summary>
    public ImdbListItems? Items { get; set; }
}
