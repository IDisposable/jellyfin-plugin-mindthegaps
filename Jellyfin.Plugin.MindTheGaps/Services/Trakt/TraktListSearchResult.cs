namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// One row of a Trakt list search result.
/// </summary>
internal class TraktListSearchResult
{
    /// <summary>Gets or sets the matched list.</summary>
    public TraktList? List { get; set; }
}
