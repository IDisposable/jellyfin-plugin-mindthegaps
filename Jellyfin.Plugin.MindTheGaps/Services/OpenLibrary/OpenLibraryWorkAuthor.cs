using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// One author entry on an OpenLibrary work record (works/{key}.json), wrapping the author key reference.
/// </summary>
public class OpenLibraryWorkAuthor
{
    /// <summary>Gets or sets the author key reference.</summary>
    [JsonPropertyName("author")]
    public OpenLibraryKeyRef? Author { get; set; }
}
