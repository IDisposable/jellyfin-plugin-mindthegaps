namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// Status of the background "where to watch" enrichment pass, returned to the dashboard so it can poll
/// for progress and completion.
/// </summary>
public class AvailabilityStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether an enrichment pass is currently running.
    /// </summary>
    public bool Running { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this request started a new pass (false if one was already running).
    /// </summary>
    public bool Started { get; set; }

    /// <summary>
    /// Gets or sets the running pass's progress, from 0 to 100.
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    /// Gets or sets how many titles the running pass has looked up so far.
    /// </summary>
    public int Processed { get; set; }

    /// <summary>
    /// Gets or sets how many titles the running pass will look up in total.
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Gets or sets the message from the last completed pass (how many were looked up and how many remain).
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
