using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// The Discogs search response (database/search), a page of results.
/// </summary>
internal class DiscogsSearchResponse
{
    /// <summary>Gets or sets the search results.</summary>
    [JsonPropertyName("results")]
    public IReadOnlyList<DiscogsSearchResult>? Results { get; set; }
}
