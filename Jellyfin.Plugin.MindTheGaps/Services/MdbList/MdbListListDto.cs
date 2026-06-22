using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.MdbList;

/// <summary>
/// An MDBList list, as returned by the search, top-lists, and list-info endpoints. Only the fields the
/// settings chip picker needs (id and name) are bound.
/// </summary>
public sealed class MdbListListDto
{
    /// <summary>Gets or sets the list id.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the list name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
