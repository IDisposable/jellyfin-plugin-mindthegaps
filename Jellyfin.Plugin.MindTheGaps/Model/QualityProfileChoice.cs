namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// One quality profile an arr (Radarr or Sonarr) offers, for the report's per-row Send picker. The property
/// names match Radarr's and Sonarr's own <c>/api/v3/qualityprofile</c> response (deserialized directly from
/// it, case insensitively), and are also what <see cref="Api.AcquisitionController"/>'s picker endpoint
/// serializes back to the client.
/// </summary>
public sealed class QualityProfileChoice
{
    /// <summary>
    /// Gets or sets the profile id, what a send's <c>qualityProfileId</c> field expects.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the profile's display name (for example "HD-1080p").
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
