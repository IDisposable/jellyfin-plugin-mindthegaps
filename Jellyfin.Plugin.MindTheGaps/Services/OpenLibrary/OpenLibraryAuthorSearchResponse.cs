using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// The OpenLibrary author-search response (search/authors.json?q={name}).
/// </summary>
public class OpenLibraryAuthorSearchResponse
{
    /// <summary>Gets or sets the matching author documents, best match first.</summary>
    [JsonPropertyName("docs")]
    public IReadOnlyList<OpenLibraryAuthorDoc>? Docs { get; set; }
}
