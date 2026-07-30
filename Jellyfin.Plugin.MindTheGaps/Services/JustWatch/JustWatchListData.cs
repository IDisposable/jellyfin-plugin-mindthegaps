namespace Jellyfin.Plugin.MindTheGaps.Services.JustWatch;

/// <summary>
/// The <c>data</c> payload of a list read.
/// </summary>
internal sealed class JustWatchListData
{
    /// <summary>
    /// Gets or sets the requested list page.
    /// </summary>
    public JustWatchTitleList? TitleListV2 { get; set; }
}
