using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// An author named on a work from an OpenLibrary subject page (subjects/{subject}.json), so a book gap can
/// read "Title by Author".
/// </summary>
internal class OpenLibrarySubjectAuthor
{
    /// <summary>Gets or sets the author key (for example "/authors/OL34221A").</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>Gets or sets the author's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
