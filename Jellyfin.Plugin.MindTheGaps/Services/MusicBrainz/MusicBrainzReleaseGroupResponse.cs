using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Services.MusicBrainz;

/// <summary>
/// The MusicBrainz "browse release-groups by artist" response shape
/// (ws/2/release-group?artist={mbid}).
/// </summary>
internal class MusicBrainzReleaseGroupResponse
{
    /// <summary>Gets or sets the total matching release-group count (for paging).</summary>
    [JsonPropertyName("release-group-count")]
    public int ReleaseGroupCount { get; set; }

    /// <summary>Gets or sets the release-groups on this page.</summary>
    [JsonPropertyName("release-groups")]
    public IReadOnlyList<MusicBrainzReleaseGroup>? ReleaseGroups { get; set; }
}
