using Jellyfin.Plugin.MindTheGaps.Configuration;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Which parts of the web UI are on, read from the configuration on every request so a change needs no
/// restart. The master switch decides only whether the client script is added to jellyfin-web. Each surface's
/// data endpoint is governed by its own placement and nothing else, <c>none</c> being off, so another client
/// can use a surface without the script ever being injected.
/// </summary>
internal static class WebUiGate
{
    /// <summary>
    /// Determines whether the client script is added to jellyfin-web's index.html and served.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when the master switch is on.</returns>
    public static bool ScriptInjected(PluginConfiguration? config) => config?.WebUiEnabled == true;

    /// <summary>
    /// Determines whether the person page's "Missing from your library" data is served.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when the section has a placement other than none.</returns>
    public static bool PersonPage(PluginConfiguration? config) => PersonPlacement.Of(config) != PlacementValue.None;

    /// <summary>
    /// Determines whether the item page's "you don't have" data (related titles, an artist's albums, an
    /// author's books) is served.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when the row has a placement other than none.</returns>
    public static bool ItemPage(PluginConfiguration? config) => ItemPlacement.Of(config) != PlacementValue.None;

    /// <summary>
    /// Determines whether a movie studio's list page's "Missing from this studio" data is served.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when the row has a placement other than none.</returns>
    public static bool StudioPage(PluginConfiguration? config) => StudioPlacement.Of(config) != PlacementValue.None;

    /// <summary>
    /// Determines whether the home screen's Discover data is served.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when the row has a placement other than none.</returns>
    public static bool HomeDiscover(PluginConfiguration? config) => HomePlacement.Discover(config) != PlacementValue.None;

    /// <summary>
    /// Determines whether the home screen's Want to watch row is served. It needs want to watch, and a
    /// placement other than none.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when both are.</returns>
    public static bool HomeWanted(PluginConfiguration? config) => WantToWatch(config) && HomePlacement.Wanted(config) != PlacementValue.None;

    /// <summary>
    /// Determines whether the bookmark on a card and the home screen's want-to-watch row are served.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when the want-to-watch toggle is on.</returns>
    public static bool WantToWatch(PluginConfiguration? config) => config?.WantToWatchEnabled == true;

    /// <summary>
    /// Determines whether a user may say they are not interested in a title on the surfaces, and so whether the
    /// surfaces leave out what they said that about.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when the not-interested toggle is on.</returns>
    public static bool NotInterested(PluginConfiguration? config) => config?.NotInterestedEnabled == true;

    /// <summary>
    /// Determines whether a movie or series page's bookmark, which keeps an owned title on the user's
    /// want-to-watch playlist, is served. It needs want to watch, its playlist, and its own toggle.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when all three are on.</returns>
    public static bool DetailBookmark(PluginConfiguration? config)
        => config is { WantToWatchEnabled: true, WantToWatchPlaylistEnabled: true, WantToWatchDetailBookmark: true };
}
