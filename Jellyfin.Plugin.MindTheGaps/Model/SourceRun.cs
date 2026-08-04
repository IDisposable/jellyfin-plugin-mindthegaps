namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// What one discovery source did on the scan that produced the report.
/// </summary>
/// <remarks>
/// Without this, a list that is read and holds nothing you are missing looks exactly like a list that was
/// never read: no section, no row, no word. The report carries the outcome so the dashboard can tell the
/// three apart, since "you own all of it", "it could not be read this scan", and "it is not switched on"
/// call for three different reactions.
/// </remarks>
public class SourceRun
{
    /// <summary>
    /// Gets or sets the discovery kind, matching <see cref="GapItem.SourceItemType"/> and the kinds the
    /// summary serves.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source's display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how many gaps it produced.
    /// </summary>
    public int Gaps { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the source threw before finishing, so its gaps (if any) are
    /// a partial read rather than the whole list.
    /// </summary>
    public bool Failed { get; set; }
}
