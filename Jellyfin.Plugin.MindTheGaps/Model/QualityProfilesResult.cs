using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The quality profiles offered for a title's kind, for the report's per-row Send picker.
/// </summary>
public sealed class QualityProfilesResult
{
    /// <summary>
    /// Gets or sets the profiles the target arr offers.
    /// </summary>
    public IReadOnlyList<QualityProfileChoice> Profiles { get; set; } = Array.Empty<QualityProfileChoice>();

    /// <summary>
    /// Gets or sets the configured default profile id, preselected in the picker.
    /// </summary>
    public int? DefaultId { get; set; }
}
