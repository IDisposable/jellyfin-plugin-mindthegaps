using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// The OpenLibrary subject response (subjects/{subject}.json), a page of works tagged with a subject.
/// </summary>
internal class OpenLibrarySubjectResponse
{
    /// <summary>Gets or sets the subject key (for example "/subjects/science_fiction").</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>Gets or sets the subject's display name (for example "Science Fiction").</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the total number of works tagged with this subject.</summary>
    [JsonPropertyName("work_count")]
    public int WorkCount { get; set; }

    /// <summary>Gets or sets the works on this page.</summary>
    [JsonPropertyName("works")]
    public IReadOnlyList<OpenLibrarySubjectWork>? Works { get; set; }
}
