namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// Status of the background mint runner, returned to the UI so it can poll for completion.
/// </summary>
public class MintStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether a mint operation is currently running.
    /// </summary>
    public bool Running { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this request started a new operation (false if one was already running).
    /// </summary>
    public bool Started { get; set; }

    /// <summary>
    /// Gets or sets the running operation's progress, from 0 to 100.
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    /// Gets or sets the message from the last completed operation.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
