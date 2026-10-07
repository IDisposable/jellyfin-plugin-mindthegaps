using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Configuration;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Whether an item page shows its "you don't have" row, and where, as the client script reads it: a side and
/// one of jellyfin-web's own item page sections. Those sections come in a fixed order and each is shown or
/// hidden by the page for the item at hand, so the row is placed by the section's name, not by a position the
/// server could only guess at.
/// </summary>
internal static class ItemPlacement
{
    /// <summary>
    /// The placement a configuration saved before there was a choice gets when its toggle was on: after
    /// "More Like This".
    /// </summary>
    public const string Default = "after:similar";

    /// <summary>
    /// The placements the settings form offers, off first and then in page order.
    /// </summary>
    public static readonly IReadOnlyList<string> Placements = new[]
    {
        PlacementValue.None,
        "before:children",
        "after:children",
        "after:cast",
        "before:similar",
        Default,
    };

    /// <summary>
    /// Reads the configured placement. An empty or unknown value is a configuration saved before the
    /// placement could switch the row off, read from the toggle it replaced.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns>One of <see cref="Placements"/>.</returns>
    public static string Of(PluginConfiguration? config)
        => PlacementValue.Match(config?.ItemPagePlacement, Placements)
            ?? (config?.ItemPageEnabled == true ? Default : PlacementValue.None);
}
