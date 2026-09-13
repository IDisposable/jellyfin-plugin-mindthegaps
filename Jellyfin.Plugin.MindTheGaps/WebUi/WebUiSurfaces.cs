namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Which web UI surfaces are switched on, so the client script renders only those.
/// </summary>
public sealed class WebUiSurfaces
{
    /// <summary>
    /// Gets or sets a value indicating whether person pages get the "Missing from your library" section.
    /// </summary>
    public bool PersonPage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether movie and series pages get the "More like this you don't have" row.
    /// </summary>
    public bool ItemPage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the home screen gets the Discover row.
    /// </summary>
    public bool HomeRow { get; set; }
}
