using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.MdbList;

/// <summary>
/// The paging block on an MDBList items response. The default page is large (1000), so a list only pages
/// when it is genuinely long, but <see cref="HasMore"/> is the only signal that it did.
/// </summary>
internal sealed class MdbListPagination
{
    /// <summary>Gets or sets the offset this page starts at.</summary>
    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    /// <summary>Gets or sets how many items a page holds.</summary>
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    /// <summary>Gets or sets how many items the whole list holds.</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>Gets or sets a value indicating whether another page follows.</summary>
    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }
}
