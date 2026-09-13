using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.PersonPage;

/// <summary>
/// The quality profiles each configured acquisition target offers, with the plugin's configured default for
/// each, so the person page can offer a choice before sending.
/// </summary>
public sealed class AcquisitionProfiles
{
    /// <summary>
    /// Gets or sets Radarr's profiles; empty when Radarr is not configured or unreachable.
    /// </summary>
    public IReadOnlyList<QualityProfileChoice> Radarr { get; set; } = Array.Empty<QualityProfileChoice>();

    /// <summary>
    /// Gets or sets the configured default Radarr profile id.
    /// </summary>
    public int RadarrDefault { get; set; }

    /// <summary>
    /// Gets or sets Sonarr's profiles; empty when Sonarr is not configured or unreachable.
    /// </summary>
    public IReadOnlyList<QualityProfileChoice> Sonarr { get; set; } = Array.Empty<QualityProfileChoice>();

    /// <summary>
    /// Gets or sets the configured default Sonarr profile id.
    /// </summary>
    public int SonarrDefault { get; set; }
}
