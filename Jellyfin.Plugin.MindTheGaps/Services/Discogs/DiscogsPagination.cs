using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// The pagination block on a paged Discogs response.
/// </summary>
internal class DiscogsPagination
{
    /// <summary>Gets or sets the total number of pages.</summary>
    [JsonPropertyName("pages")]
    public int Pages { get; set; }

    /// <summary>Gets or sets the current page number (one-based).</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }
}
