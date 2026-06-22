using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.MdbList;

/// <summary>
/// One item on an MDBList list. The list already records the external ids (TMDB, IMDb, TheTVDB), so an
/// item drops straight into the ownership diff and link building without a separate id-resolution step.
/// </summary>
public sealed class MdbListItem
{
    /// <summary>Gets or sets the title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the IMDb id (for example "tt0111161"), when present.</summary>
    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    /// <summary>Gets or sets the TheTVDB id, when present.</summary>
    [JsonPropertyName("tvdb_id")]
    public int? TvdbId { get; set; }

    /// <summary>Gets or sets the TMDB id, when present.</summary>
    [JsonPropertyName("tmdb_id")]
    public int? TmdbId { get; set; }

    /// <summary>Gets or sets the media type ("movie" or "show"). Filled in from the owning array when blank.</summary>
    [JsonPropertyName("mediatype")]
    public string? MediaType { get; set; }

    /// <summary>Gets or sets the release year, when present.</summary>
    [JsonPropertyName("release_year")]
    public int? ReleaseYear { get; set; }
}
