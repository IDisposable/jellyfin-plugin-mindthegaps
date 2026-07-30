namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The state of the background bulk re-check, so the dashboard can show its progress and reload the report
/// when it finishes.
/// </summary>
public class RecheckStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether a bulk re-check is currently running.
    /// </summary>
    public bool Running { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this request started a run (false if one was already running).
    /// </summary>
    public bool Started { get; set; }

    /// <summary>
    /// Gets or sets the running re-check's progress, from 0 to 100.
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    /// Gets or sets how many owning items the run was asked to cover.
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Gets or sets how many owning items the run has finished.
    /// </summary>
    public int Done { get; set; }
}
