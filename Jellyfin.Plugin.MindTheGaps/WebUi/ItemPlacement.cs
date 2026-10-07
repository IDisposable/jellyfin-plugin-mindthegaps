using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Configuration;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Where an item page's "you don't have" row goes, as the client script reads it: a side and one of
/// jellyfin-web's own item page sections. Those sections come in a fixed order and each is shown or hidden
/// by the page for the item at hand, so the row is placed by the section's name, not by a position the
/// server could only guess at.
/// </summary>
internal static class ItemPlacement
{
    /// <summary>
    /// The placement used when none is configured: after "More Like This".
    /// </summary>
    public const string Default = "after:similar";

    /// <summary>
    /// The placements the script knows the sections of, in page order.
    /// </summary>
    public static readonly IReadOnlyList<string> Placements = new[]
    {
        "before:children",
        "after:children",
        "after:cast",
        "before:similar",
        Default,
    };

    /// <summary>
    /// Reads the configured placement, folding anything unrecognized to <see cref="Default"/>.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns>One of <see cref="Placements"/>.</returns>
    public static string Of(PluginConfiguration? config)
    {
        var value = config?.ItemPagePlacement?.Trim() ?? string.Empty;
        return Placements.FirstOrDefault(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase)) ?? Default;
    }
}
