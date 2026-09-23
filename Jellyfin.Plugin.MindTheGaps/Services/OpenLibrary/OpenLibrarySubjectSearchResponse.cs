using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// The OpenLibrary subject search response (search/subjects.json?q=...).
/// </summary>
internal class OpenLibrarySubjectSearchResponse
{
    /// <summary>Gets or sets the matching subjects.</summary>
    [JsonPropertyName("docs")]
    public IReadOnlyList<OpenLibrarySubjectSearchDoc>? Docs { get; set; }
}
