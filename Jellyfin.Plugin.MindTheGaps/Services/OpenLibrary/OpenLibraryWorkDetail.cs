using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// An OpenLibrary work record (works/{key}.json), used to read a work's authors directly so an owned book
/// resolves its author without a name search (which hits the namesake problem).
/// </summary>
internal class OpenLibraryWorkDetail
{
    /// <summary>Gets or sets the work's authors.</summary>
    [JsonPropertyName("authors")]
    public IReadOnlyList<OpenLibraryWorkAuthor>? Authors { get; set; }
}
