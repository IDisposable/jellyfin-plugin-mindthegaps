using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// An OpenLibrary "work" (the abstract book, independent of its individual editions).
/// </summary>
public class OpenLibraryWork
{
    /// <summary>Gets or sets the work key (for example "/works/OL45804W").</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>Gets or sets the title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the first publish date, when present (free-form, often just a year).</summary>
    [JsonPropertyName("first_publish_date")]
    public string? FirstPublishDate { get; set; }
}
