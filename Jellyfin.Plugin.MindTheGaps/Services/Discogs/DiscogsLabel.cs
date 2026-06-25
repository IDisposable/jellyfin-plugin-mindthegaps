using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// A Discogs label record (labels/{id}), used to resolve a stored label id back to its name for the
/// settings chip picker.
/// </summary>
internal class DiscogsLabel
{
    /// <summary>Gets or sets the Discogs label id.</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Gets or sets the label name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
