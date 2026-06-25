using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// One work-level document from OpenLibrary's search endpoint (search.json). Unlike the author-works list,
/// search results carry the first publish year, so book gaps can get a year in a single call.
/// </summary>
internal class OpenLibrarySearchDoc
{
    /// <summary>Gets or sets the work key (for example "/works/OL45804W").</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>Gets or sets the title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the first publish year, when present.</summary>
    [JsonPropertyName("first_publish_year")]
    public int? FirstPublishYear { get; set; }
}
