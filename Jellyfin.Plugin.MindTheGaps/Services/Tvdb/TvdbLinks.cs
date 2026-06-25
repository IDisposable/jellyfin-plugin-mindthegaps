namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// Pagination links on a TheTVDB list response.
/// </summary>
internal class TvdbLinks
{
    /// <summary>Gets or sets the URL of the next page, or null on the last page.</summary>
    public string? Next { get; set; }
}
