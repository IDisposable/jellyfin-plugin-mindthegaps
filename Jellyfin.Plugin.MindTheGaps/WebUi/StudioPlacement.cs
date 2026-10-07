using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Configuration;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Whether a studio's list page shows "Missing from &lt;studio&gt;". The page is jellyfin-web's generic list
/// page and holds nothing but the studio's movies, so the row has one place, below them.
/// </summary>
internal static class StudioPlacement
{
    /// <summary>
    /// Below the studio's movies.
    /// </summary>
    public const string Shown = "after:movies";

    /// <summary>
    /// The placements the settings form offers, off first.
    /// </summary>
    public static readonly IReadOnlyList<string> Placements = new[] { PlacementValue.None, Shown };

    /// <summary>
    /// Reads the configured placement. An empty or unknown value is a configuration saved before the placement
    /// existed, read from the toggle it replaced.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns>One of <see cref="Placements"/>.</returns>
    public static string Of(PluginConfiguration? config)
        => PlacementValue.Match(config?.StudioPagePlacement, Placements)
            ?? (config?.StudioPageEnabled == true ? Shown : PlacementValue.None);
}
