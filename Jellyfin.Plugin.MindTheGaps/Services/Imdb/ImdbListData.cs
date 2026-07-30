namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// The <c>data</c> payload of a list read. Both queries the plugin issues return a list under a different
/// root field, so one type covers both and the caller takes whichever is populated.
/// </summary>
internal sealed class ImdbListData
{
    /// <summary>
    /// Gets or sets the list a "ls" id resolved to.
    /// </summary>
    public ImdbList? List { get; set; }

    /// <summary>
    /// Gets or sets the watchlist a "ur" user id resolved to.
    /// </summary>
    public ImdbList? PredefinedList { get; set; }
}
