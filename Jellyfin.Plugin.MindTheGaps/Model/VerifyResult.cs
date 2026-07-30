using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The outcome of a verify pass: how many of the submitted gaps were checked against the library, and which
/// of them the library now holds (and were therefore dropped from the report).
/// </summary>
public class VerifyResult
{
    /// <summary>
    /// Gets or sets how many of the submitted ids matched a gap still in the report and were checked. Ids the
    /// report no longer holds are skipped rather than counted.
    /// </summary>
    public int Checked { get; set; }

    /// <summary>
    /// Gets or sets how many of the checked gaps the library now holds.
    /// </summary>
    public int Owned { get; set; }

    /// <summary>
    /// Gets or sets how many gaps were removed in total. This is at least <see cref="Owned"/> and usually
    /// more: one acquired title is a gap in every set that wanted it, so confirming it drops all of them,
    /// including rows on tabs the client has not loaded.
    /// </summary>
    public int Removed { get; set; }

    /// <summary>
    /// Gets or sets the ids of every gap removed, so the dashboard can drop exactly those rows without
    /// reloading the whole report.
    /// </summary>
    public IReadOnlyList<string> RemovedIds { get; set; } = [];
}
