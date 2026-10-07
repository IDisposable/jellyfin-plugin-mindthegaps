using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Configuration;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Where the home screen's rows go, as the client script reads it. jellyfin-web lays the home screen out as
/// numbered slots, each holding one section type the user chose in their home screen settings, and has no
/// way to add a section type of its own, so the rows follow a chosen type to wherever each user put it.
/// </summary>
internal static class HomePlacement
{
    /// <summary>
    /// The value that puts the rows above every home section.
    /// </summary>
    public const string Top = "top";

    /// <summary>
    /// An empty home slot.
    /// </summary>
    public const string None = "none";

    /// <summary>
    /// The section types jellyfin-web's <c>HomeSectionType</c> names, in its default order; the rows can
    /// follow any of them.
    /// </summary>
    public static readonly IReadOnlyList<string> SectionTypes = new[]
    {
        "smalllibrarytiles",
        "librarybuttons",
        "resume",
        "resumeaudio",
        "resumebook",
        "livetv",
        "activerecordings",
        "nextup",
        "latestmedia",
    };

    /// <summary>
    /// What jellyfin-web shows in a home slot the user never set, slot by slot (its <c>DEFAULT_SECTIONS</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultOrder = new[]
    {
        "smalllibrarytiles",
        "resume",
        "resumeaudio",
        "resumebook",
        "livetv",
        "nextup",
        "latestmedia",
        None,
        None,
        None,
    };

    /// <summary>
    /// Determines whether a placement follows a section type, and so needs the viewing user's slot order.
    /// </summary>
    /// <param name="placement">A value <see cref="Of"/> returned.</param>
    /// <returns><see langword="true"/> for a section type.</returns>
    public static bool FollowsSection(string placement) => placement.Length > 0 && placement != Top;

    /// <summary>
    /// The user's home slots in order, in jellyfin-web's spelling, as its home screen reads them: a slot the
    /// user set holds what they chose, any other the default for that slot.
    /// </summary>
    /// <param name="stored">The slots the user saved, by order and the type's name.</param>
    /// <returns>One entry per slot.</returns>
    public static IReadOnlyList<string> Resolve(IEnumerable<(int Order, string Type)> stored)
    {
        var order = DefaultOrder.ToArray();
        foreach (var (slot, type) in stored)
        {
            if (slot >= 0 && slot < order.Length)
            {
                order[slot] = SectionTypes.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase)) ?? None;
            }
        }

        return order;
    }

    /// <summary>
    /// Reads the configured placement, folding anything unrecognized to the bottom of the page.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see cref="Top"/>, a section type, or an empty string for the bottom.</returns>
    public static string Of(PluginConfiguration? config)
    {
        var value = config?.HomeRowPlacement?.Trim() ?? string.Empty;
        if (string.Equals(value, Top, StringComparison.OrdinalIgnoreCase))
        {
            return Top;
        }

        return SectionTypes.FirstOrDefault(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }
}
