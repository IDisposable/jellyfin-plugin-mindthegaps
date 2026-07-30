using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// One entry on a Discogs wantlist.
/// </summary>
internal sealed class DiscogsWant
{
    /// <summary>Gets or sets the Discogs release id (the same id as the release summary carries).</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Gets or sets the release summary.</summary>
    [JsonPropertyName("basic_information")]
    public DiscogsBasicInformation? BasicInformation { get; set; }
}
