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

    /// <summary>
    /// Gets or sets a value indicating whether the want-to-watch list is on (the home row and the marks).
    /// </summary>
    public bool WantToWatch { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the caller may add to and remove from the want-to-watch list.
    /// </summary>
    public bool CanEditWantToWatch { get; set; }
}
