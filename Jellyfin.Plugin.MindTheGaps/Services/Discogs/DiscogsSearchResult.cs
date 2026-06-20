using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// One result from the Discogs search endpoint (database/search), used to resolve a label name to its id.
/// </summary>
public class DiscogsSearchResult
{
    /// <summary>Gets or sets the Discogs id (a label id when searching labels).</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Gets or sets the title (the label name when searching labels).</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
