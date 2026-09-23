using Jellyfin.Plugin.MindTheGaps.Configuration;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Which parts of the web UI are on, read from the configuration on every request so a change needs no
/// restart. The master switch decides only whether the client script is added to jellyfin-web. Each surface's
/// data endpoint is governed by its own toggle and nothing else, so another client can use a surface without
/// the script ever being injected.
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
    /// <returns><see langword="true"/> when the person page toggle is on.</returns>
    public static bool PersonPage(PluginConfiguration? config) => config?.PersonPageEnabled == true;

    /// <summary>
    /// Determines whether the movie and series page's "More like this you don't have" data is served.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when the item page toggle is on.</returns>
    public static bool ItemPage(PluginConfiguration? config) => config?.ItemPageEnabled == true;

    /// <summary>
    /// Determines whether a movie studio's list page's "Missing from this studio" data is served.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when the studio page toggle is on.</returns>
    public static bool StudioPage(PluginConfiguration? config) => config?.StudioPageEnabled == true;

    /// <summary>
    /// Determines whether the home screen's Discover data is served.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when the home row toggle is on.</returns>
    public static bool HomeRow(PluginConfiguration? config) => config?.HomeRowEnabled == true;

    /// <summary>
    /// Determines whether the bookmark on a card and the home screen's want-to-watch row are served.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when the want-to-watch toggle is on.</returns>
    public static bool WantToWatch(PluginConfiguration? config) => config?.WantToWatchEnabled == true;
}
