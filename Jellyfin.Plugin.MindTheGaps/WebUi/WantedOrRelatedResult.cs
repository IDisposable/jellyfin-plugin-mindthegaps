namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// What an owned movie or series page shows from the plugin, in one response: its bookmark's state and the
/// similar titles the library does not hold. Each part has its own toggle and is null when it is off or does
/// not apply.
/// </summary>
public sealed class WantedOrRelatedResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the title is on the caller's want-to-watch playlist; null when
    /// the page bookmark is not offered.
    /// </summary>
    public bool? OnList { get; set; }

    /// <summary>
    /// Gets or sets the unowned similar titles; null while the item page surface is off.
    /// </summary>
    public RelatedMissingResult? Related { get; set; }
}
