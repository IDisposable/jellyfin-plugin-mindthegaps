using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// A release listed on a Discogs record label (labels/{id}/releases) or an artist
/// (artists/{id}/releases). The label endpoint omits <see cref="Type"/>, <see cref="Role"/>, and
/// <see cref="MainRelease"/>, which are present on the artist endpoint.
/// </summary>
internal class DiscogsRelease
{
    /// <summary>Gets or sets the Discogs release (or master) id.</summary>
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

    /// <summary>Gets or sets the entry type on an artist's releases ("master" or "release"); null on the label endpoint.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Gets or sets the artist's role on an artist's releases ("Main", "Appearance", ...); null on the label endpoint.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>Gets or sets the canonical release id for a master entry, when present.</summary>
    [JsonPropertyName("main_release")]
    public long? MainRelease { get; set; }
}
