using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The quality profiles offered for a title's kind (Radarr for a movie, Sonarr for a series), for the
/// detail dialog's picker. Empty when the matching arr is not configured.
/// </summary>
public sealed class QualityProfilesResult
{
    /// <summary>
    /// Gets or sets the profiles.
    /// </summary>
    public IReadOnlyList<QualityProfileChoice> Profiles { get; set; } = Array.Empty<QualityProfileChoice>();

    /// <summary>
    /// Gets or sets the configured default profile id, preselected in the picker.
    /// </summary>
    public int DefaultId { get; set; }
}
