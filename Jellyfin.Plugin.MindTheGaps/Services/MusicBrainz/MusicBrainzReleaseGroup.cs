using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.MusicBrainz;

/// <summary>
/// A MusicBrainz release-group (the abstract "album", independent of its individual releases).
/// </summary>
public class MusicBrainzReleaseGroup
{
    /// <summary>Gets or sets the release-group MBID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the primary type (Album, Single, EP, Broadcast, Other).</summary>
    [JsonPropertyName("primary-type")]
    public string? PrimaryType { get; set; }

    /// <summary>
    /// Gets or sets the secondary types (Compilation, Live, Soundtrack, Remix, ...). A studio album has
    /// none of these.
    /// </summary>
    [JsonPropertyName("secondary-types")]
    public IReadOnlyList<string>? SecondaryTypes { get; set; }

    /// <summary>Gets or sets the earliest release date (yyyy, yyyy-MM, or yyyy-MM-dd).</summary>
    [JsonPropertyName("first-release-date")]
    public string? FirstReleaseDate { get; set; }
}
