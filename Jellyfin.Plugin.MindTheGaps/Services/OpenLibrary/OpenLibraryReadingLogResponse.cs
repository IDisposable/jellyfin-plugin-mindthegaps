using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// A page of a reader's public reading log (the "Want to Read", "Currently Reading", or "Already Read"
/// shelf).
/// </summary>
internal sealed class OpenLibraryReadingLogResponse
{
    /// <summary>
    /// Gets or sets the 1-based page number this response is.
    /// </summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>
    /// Gets or sets how many entries the whole shelf holds.
    /// </summary>
    [JsonPropertyName("numFound")]
    public int NumFound { get; set; }

    /// <summary>
    /// Gets or sets this page's entries.
    /// </summary>
    [JsonPropertyName("reading_log_entries")]
    public IReadOnlyList<OpenLibraryReadingLogEntry>? ReadingLogEntries { get; set; }
}
