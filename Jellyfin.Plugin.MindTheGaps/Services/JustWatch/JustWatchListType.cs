using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MindTheGaps.Services.JustWatch;

/// <summary>
/// The JustWatch account lists the plugin can read, spelled as the API's <c>TitleListTypeV2</c> enum. Only
/// the want-lists are here: a seen list is the opposite of a gap.
/// </summary>
internal static class JustWatchListType
{
    /// <summary>The Watchlist, the "My Lists" tab this plugin is for.</summary>
    public const string Watchlist = "WATCHLIST";

    /// <summary>The Likelist, the titles marked with a thumbs up.</summary>
    public const string Likelist = "LIKELIST";

    /// <summary>
    /// Gets the readable list types, in the order a scan walks them.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Watchlist, Likelist];

    /// <summary>
    /// Gets the display name of a list type, for the gap's source label.
    /// </summary>
    /// <param name="listType">A value from <see cref="All"/>.</param>
    /// <returns>The display name.</returns>
    public static string DisplayName(string listType)
        => string.Equals(listType, Likelist, StringComparison.Ordinal) ? "JustWatch likes" : "JustWatch watchlist";

    /// <summary>
    /// Determines whether a value names a readable list type.
    /// </summary>
    /// <param name="listType">The candidate value, or null.</param>
    /// <returns><see langword="true"/> when readable.</returns>
    public static bool IsKnown(string? listType)
        => listType is not null && All.Contains(listType, StringComparer.Ordinal);
}
