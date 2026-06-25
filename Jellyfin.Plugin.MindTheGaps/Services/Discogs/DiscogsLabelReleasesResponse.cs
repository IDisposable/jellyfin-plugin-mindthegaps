using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// The Discogs label-releases response (labels/{id}/releases), a page of releases plus pagination.
/// </summary>
internal class DiscogsLabelReleasesResponse
{
    /// <summary>Gets or sets the pagination block.</summary>
    [JsonPropertyName("pagination")]
    public DiscogsPagination? Pagination { get; set; }

    /// <summary>Gets or sets the releases on this page.</summary>
    [JsonPropertyName("releases")]
    public IReadOnlyList<DiscogsRelease>? Releases { get; set; }
}
