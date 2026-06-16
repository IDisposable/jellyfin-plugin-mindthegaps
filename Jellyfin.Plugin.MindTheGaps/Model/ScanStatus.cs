namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// Status of the background gap scan, returned to the dashboard so it can poll for completion.
/// </summary>
public class ScanStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether a scan is currently running.
    /// </summary>
    public bool Running { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this request started a new scan (false if one was already running).
    /// </summary>
    public bool Started { get; set; }

    /// <summary>
    /// Gets or sets the running scan's progress, from 0 to 100.
    /// </summary>
    public double Progress { get; set; }
}
