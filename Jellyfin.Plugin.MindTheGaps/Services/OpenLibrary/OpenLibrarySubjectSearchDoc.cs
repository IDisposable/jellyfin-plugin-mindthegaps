using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// One matched subject in an OpenLibrary subject search result.
/// </summary>
internal class OpenLibrarySubjectSearchDoc
{
    /// <summary>Gets or sets the subject key (for example "/subjects/science_fiction").</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>Gets or sets the subject's display name (for example "Science fiction").</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
