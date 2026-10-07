using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Configuration;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Whether a person page shows "Missing from your library". It has one place, below the person's own titles.
/// </summary>
internal static class PersonPlacement
{
    /// <summary>
    /// Below the person's own titles.
    /// </summary>
    public const string Shown = "after:credits";

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
        => PlacementValue.Match(config?.PersonPagePlacement, Placements)
            ?? (config?.PersonPageEnabled == true ? Shown : PlacementValue.None);
}
