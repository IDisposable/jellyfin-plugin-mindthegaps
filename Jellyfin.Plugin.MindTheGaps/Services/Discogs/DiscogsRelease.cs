using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// A release listed on a Discogs record label (labels/{id}/releases).
/// </summary>
public class DiscogsRelease
{
    /// <summary>Gets or sets the Discogs release id.</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Gets or sets the release title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the credited artist.</summary>
    [JsonPropertyName("artist")]
    public string? Artist { get; set; }

    /// <summary>Gets or sets the release year, when present (0 when unknown).</summary>
    [JsonPropertyName("year")]
    public int Year { get; set; }
}
