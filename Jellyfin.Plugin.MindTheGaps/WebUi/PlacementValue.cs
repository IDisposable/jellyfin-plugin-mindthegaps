using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Reads a placement setting. Every Web UI surface is switched by its placement, "none" being off, so there is
/// no separate toggle to disagree with it.
/// </summary>
internal static class PlacementValue
{
    /// <summary>
    /// The placement that switches a surface off.
    /// </summary>
    public const string None = "none";

    /// <summary>
    /// Matches a configured value against a setting's placements, ignoring case and surrounding space.
    /// </summary>
    /// <param name="value">The configured value.</param>
    /// <param name="placements">The placements the setting offers.</param>
    /// <returns>The placement in its own spelling, or <see langword="null"/> for an empty or unknown value,
    /// which the caller reads from the toggle the setting replaced.</returns>
    public static string? Match(string? value, IReadOnlyList<string> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);

        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? null
            : placements.FirstOrDefault(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
