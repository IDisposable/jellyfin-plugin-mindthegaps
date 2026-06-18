using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// A single author hit from OpenLibrary's author search.
/// </summary>
public class OpenLibraryAuthorDoc
{
    /// <summary>Gets or sets the author key (for example "OL23919A").</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>Gets or sets the author's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the number of works attributed to this author.</summary>
    [JsonPropertyName("work_count")]
    public int WorkCount { get; set; }
}
