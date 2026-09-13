namespace Jellyfin.Plugin.MindTheGaps.Services.Acquisition;

/// <summary>
/// One arr entry's state.
/// </summary>
public sealed class ArrItemState
{
    /// <summary>
    /// Gets or sets a value indicating whether a file has been downloaded.
    /// </summary>
    public bool HasFile { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the entry is monitored (will be grabbed when available).
    /// </summary>
    public bool Monitored { get; set; }
}
