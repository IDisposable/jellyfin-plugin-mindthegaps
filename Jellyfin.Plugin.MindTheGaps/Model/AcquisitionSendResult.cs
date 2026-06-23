namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The aggregate result of a handoff send returned to the dashboard: how many of the requested items were
/// accepted, how many failed, and a short summary. A single-row send reports one success or one failure.
/// </summary>
public sealed class AcquisitionSendResult
{
    /// <summary>
    /// Gets or sets a value indicating whether every requested send succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the number of items the target accepted.
    /// </summary>
    public int Succeeded { get; set; }

    /// <summary>
    /// Gets or sets the number of items that failed to send.
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// Gets or sets the summary message (for example "Sent 3 item(s)." or "Sent 2, 1 failed. First failure: ...").
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
