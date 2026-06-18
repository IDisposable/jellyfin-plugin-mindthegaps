using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// The OpenLibrary author-works response (authors/{key}/works.json).
/// </summary>
public class OpenLibraryWorksResponse
{
    /// <summary>Gets or sets the total work count.</summary>
    [JsonPropertyName("size")]
    public int Size { get; set; }

    /// <summary>Gets or sets the work entries on this page.</summary>
    [JsonPropertyName("entries")]
    public IReadOnlyList<OpenLibraryWork>? Entries { get; set; }
}
