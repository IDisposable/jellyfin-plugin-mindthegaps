namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// Tells the dashboard which acquisition targets are configured, so a per-row Send button appears only for a
/// target that is set up. The keys and URLs themselves are never sent to the browser.
/// </summary>
public sealed class AcquisitionConfigStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether Radarr is configured (URL, key, quality profile, root folder).
    /// </summary>
    public bool RadarrConfigured { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Sonarr is configured (URL, key, quality profile, root folder).
    /// </summary>
    public bool SonarrConfigured { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Jellyseerr/Overseerr is configured (URL and key).
    /// </summary>
    public bool SeerrConfigured { get; set; }
}
