using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// A bare OpenLibrary key reference (for example an author reference "/authors/OL79034A" inside a work).
/// </summary>
internal class OpenLibraryKeyRef
{
    /// <summary>Gets or sets the key (for example "/authors/OL79034A").</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }
}
