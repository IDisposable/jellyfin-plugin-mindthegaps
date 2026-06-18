using System;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// Request body to dismiss a gap (resolve, not-interested, or snooze) with a note.
/// </summary>
public class ResolveRequest
{
    /// <summary>
    /// Gets or sets the gap id to dismiss.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the note explaining the dismissal.
    /// </summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the dismissal kind (<see cref="GapResolution.Resolved"/>,
    /// <see cref="GapResolution.NotInterested"/>, or <see cref="GapResolution.Snoozed"/>). Defaults to
    /// resolved when omitted.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// Gets or sets the UTC time a snoozed gap should resurface (for <see cref="GapResolution.Snoozed"/>).
    /// </summary>
    public DateTime? SnoozedUntil { get; set; }
}
