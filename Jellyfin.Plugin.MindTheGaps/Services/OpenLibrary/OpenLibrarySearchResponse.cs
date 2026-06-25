using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// The OpenLibrary search response (search.json), a page of work-level documents.
/// </summary>
internal class OpenLibrarySearchResponse
{
    /// <summary>Gets or sets the matching work documents.</summary>
    [JsonPropertyName("docs")]
    public IReadOnlyList<OpenLibrarySearchDoc>? Docs { get; set; }
}
