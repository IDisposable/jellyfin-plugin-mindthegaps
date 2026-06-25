using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// One work from an OpenLibrary subject page (subjects/{subject}.json). Unlike the author-works list, a
/// subject work carries its first publish year, so a book gap can get a year in a single call.
/// </summary>
internal class OpenLibrarySubjectWork
{
    /// <summary>Gets or sets the work key (for example "/works/OL45804W").</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>Gets or sets the title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the work's authors, when present.</summary>
    [JsonPropertyName("authors")]
    public IReadOnlyList<OpenLibrarySubjectAuthor>? Authors { get; set; }

    /// <summary>Gets or sets the first publish year, when present.</summary>
    [JsonPropertyName("first_publish_year")]
    public int? FirstPublishYear { get; set; }
}
