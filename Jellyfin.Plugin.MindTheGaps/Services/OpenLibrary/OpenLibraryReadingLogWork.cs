using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// The work behind one reading-log entry. A leaner shape than <see cref="OpenLibraryWork"/>: the reading log
/// carries the search-style fields (author names inline, a first-publish year rather than a free-form date),
/// so it deserializes on its own rather than being forced into the works shape.
/// </summary>
internal sealed class OpenLibraryReadingLogWork
{
    /// <summary>
    /// Gets or sets the work key ("/works/OL17363125W").
    /// </summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the author keys ("/authors/OL6575473A").
    /// </summary>
    [JsonPropertyName("author_keys")]
    public IReadOnlyList<string>? AuthorKeys { get; set; }

    /// <summary>
    /// Gets or sets the author display names.
    /// </summary>
    [JsonPropertyName("author_names")]
    public IReadOnlyList<string>? AuthorNames { get; set; }

    /// <summary>
    /// Gets or sets the first publication year.
    /// </summary>
    [JsonPropertyName("first_publish_year")]
    public int? FirstPublishYear { get; set; }

    /// <summary>
    /// Gets or sets the cover id, which the cover URL is built from.
    /// </summary>
    [JsonPropertyName("cover_id")]
    public long? CoverId { get; set; }
}
