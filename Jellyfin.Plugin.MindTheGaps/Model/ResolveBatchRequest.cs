using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// Request body to dismiss several gaps at once (the same kind and note for each), for the "resolve
/// every item under this series or season" group actions.
/// </summary>
public class ResolveBatchRequest
{
    /// <summary>
    /// Gets or sets the gap ids to dismiss.
    /// </summary>
    public IReadOnlyList<string> Ids { get; set; } = [];

    /// <summary>
    /// Gets or sets the note applied to each dismissal.
    /// </summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the dismissal kind (<see cref="GapResolution.Resolved"/>,
    /// <see cref="GapResolution.NotInterested"/>, or <see cref="GapResolution.Snoozed"/>). Defaults to
    /// resolved when omitted.
    /// </summary>
    public string? Kind { get; set; }
}
