using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.MdbList;

/// <summary>
/// The response from MDBList's list-items endpoint (lists/{id}/items), which splits a list's members into
/// a movies array and a shows array.
/// </summary>
internal sealed class MdbListItemsResponse
{
    /// <summary>Gets or sets the movie members.</summary>
    [JsonPropertyName("movies")]
    public IReadOnlyList<MdbListItem>? Movies { get; set; }

    /// <summary>Gets or sets the show members.</summary>
    [JsonPropertyName("shows")]
    public IReadOnlyList<MdbListItem>? Shows { get; set; }
}
