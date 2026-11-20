namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// What kind of list a list is: a user's own list or their watchlist. A watchlist answers with its own "ls"
/// id too, so the class is what tells the two apart when a watchlist is addressed by that id.
/// </summary>
internal sealed class ImdbListClass
{
    /// <summary>
    /// Gets or sets the class id: "LIST" or "WATCH_LIST".
    /// </summary>
    public string? Id { get; set; }
}
