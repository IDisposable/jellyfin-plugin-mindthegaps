using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// The release summary a wantlist entry carries.
/// </summary>
internal sealed class DiscogsBasicInformation
{
    /// <summary>Gets or sets the Discogs release id.</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Gets or sets the master id, 0 when the release belongs to no master.</summary>
    [JsonPropertyName("master_id")]
    public long MasterId { get; set; }

    /// <summary>Gets or sets the release title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the release year, 0 when unknown.</summary>
    [JsonPropertyName("year")]
    public int Year { get; set; }

    /// <summary>Gets or sets the credited artists.</summary>
    [JsonPropertyName("artists")]
    public IReadOnlyList<DiscogsArtistCredit>? Artists { get; set; }

    /// <summary>Gets or sets the cover image URL, when Discogs serves one.</summary>
    [JsonPropertyName("cover_image")]
    public string? CoverImage { get; set; }
}
