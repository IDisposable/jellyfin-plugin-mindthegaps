using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Configuration;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Whether each of the home screen's rows is shown, and where, as the client script reads it. jellyfin-web lays
/// the home screen out as numbered slots, each holding one section type the user chose in their home screen
/// settings, and has no way to add a section type of its own, so a row follows a chosen type to wherever each
/// user put it. The Discover and Want to watch rows are placed separately; two rows given the same placement
/// keep Want to watch first.
/// </summary>
internal static class HomePlacement
{
    /// <summary>
    /// The value that puts a row above every home section.
    /// </summary>
    public const string Top = "top";

    /// <summary>
    /// The value that puts a row below every home section.
    /// </summary>
    public const string Bottom = "bottom";

    /// <summary>
    /// An empty home slot, in jellyfin-web's spelling.
    /// </summary>
    public const string EmptySlot = "none";

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
        EmptySlot,
        EmptySlot,
        EmptySlot,
    };

    /// <summary>
    /// The placements the settings form offers for each row: off, the bottom, the top, then after each section
    /// type.
    /// </summary>
    public static readonly IReadOnlyList<string> Placements =
        new[] { PlacementValue.None, Bottom, Top }.Concat(SectionTypes).ToArray();

    /// <summary>
    /// Determines whether a placement follows a section type, and so needs the viewing user's slot order.
    /// </summary>
    /// <param name="placement">A value <see cref="Discover"/> or <see cref="Wanted"/> returned.</param>
    /// <returns><see langword="true"/> for a section type.</returns>
    public static bool FollowsSection(string placement) => SectionTypes.Contains(placement, StringComparer.Ordinal);

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
                order[slot] = SectionTypes.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase)) ?? EmptySlot;
            }
        }

        return order;
    }

    /// <summary>
    /// Reads the Discover row's placement. An empty or unknown value is a configuration saved before the rows
    /// were placed separately, read from the toggle and the shared placement they replaced.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns>One of <see cref="Placements"/>.</returns>
    public static string Discover(PluginConfiguration? config)
        => PlacementValue.Match(config?.HomeDiscoverPlacement, Placements)
            ?? (config?.HomeRowEnabled == true ? Shared(config) : PlacementValue.None);

    /// <summary>
    /// Reads the Want to watch row's placement. An empty or unknown value is a configuration saved before the
    /// rows were placed separately, read from the shared placement; the row was shown wherever want to watch
    /// was on.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns>One of <see cref="Placements"/>.</returns>
    public static string Wanted(PluginConfiguration? config)
        => PlacementValue.Match(config?.HomeWantedPlacement, Placements) ?? Shared(config);

    // The one placement both rows shared, where empty or anything unrecognized was the bottom.
    private static string Shared(PluginConfiguration? config)
    {
        var shared = PlacementValue.Match(config?.HomeRowPlacement, Placements);
        return shared is null or PlacementValue.None ? Bottom : shared;
    }
}
