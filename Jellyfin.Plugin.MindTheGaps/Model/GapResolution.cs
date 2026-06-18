using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A per-gap dismissal: the gap is deliberately set aside. The <see cref="Kind"/> distinguishes
/// "resolved" (not really missing, for example two listed episodes that are one combined file),
/// "notinterested" (a real gap the user does not want), and "snoozed" (hidden until
/// <see cref="SnoozedUntil"/>, used to park an upcoming title until it is released). Persisted across
/// scans by <see cref="GapItem.Id"/>.
/// </summary>
public class GapResolution
{
    /// <summary>
    /// The "not really missing" dismissal kind.
    /// </summary>
    public const string Resolved = "resolved";

    /// <summary>
    /// The "real gap, do not want it" dismissal kind.
    /// </summary>
    public const string NotInterested = "notinterested";

    /// <summary>
    /// The "hide until a date" dismissal kind (see <see cref="SnoozedUntil"/>).
    /// </summary>
    public const string Snoozed = "snoozed";

    /// <summary>
    /// Gets or sets the dismissal kind. Null (the common case, also entries saved before kinds existed)
    /// means <see cref="Resolved"/>; it is omitted from the stored JSON and API responses so the default
    /// carries no overhead. Only <see cref="NotInterested"/> and <see cref="Snoozed"/> are written.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    /// <summary>
    /// Gets or sets the note explaining the dismissal.
    /// </summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC time the gap was dismissed.
    /// </summary>
    public DateTime ResolvedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time a snoozed gap should resurface (its release date). Null for the other
    /// kinds, and omitted from the stored JSON when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? SnoozedUntil { get; set; }
}
