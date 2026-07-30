using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// A page of a user's Discogs wantlist.
/// </summary>
internal sealed class DiscogsWantlistResponse
{
    /// <summary>Gets or sets the pagination block.</summary>
    [JsonPropertyName("pagination")]
    public DiscogsPagination? Pagination { get; set; }

    /// <summary>Gets or sets this page's entries.</summary>
    [JsonPropertyName("wants")]
    public IReadOnlyList<DiscogsWant>? Wants { get; set; }
}
