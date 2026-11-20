using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.ListenBrainz;

/// <summary>
/// One artist ListenBrainz finds similar to a reference artist, from listening sessions.
/// </summary>
internal sealed class ListenBrainzSimilarArtist
{
    /// <summary>Gets or sets the similar artist's MusicBrainz id.</summary>
    [JsonPropertyName("artist_mbid")]
    public string? ArtistMbid { get; set; }

    /// <summary>Gets or sets the similar artist's name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets MusicBrainz's disambiguation comment ("UK rock band"), or empty.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets the similarity score; higher is closer.</summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>Gets or sets the reference artist's MusicBrainz id.</summary>
    [JsonPropertyName("reference_mbid")]
    public string? ReferenceMbid { get; set; }
}
