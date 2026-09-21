using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The cache entries the web UI services share for an owned-items index: one library read per set of kinds,
/// kept briefly so browsing several pages in a row does not re-read the library each time, while a title
/// added to the library still drops off within a minute.
/// </summary>
internal static class OwnershipCache
{
    /// <summary>
    /// The memory cache key of the owned movies-and-series index.
    /// </summary>
    public const string Key = "mtg-webui-ownership";

    /// <summary>
    /// How long an index is kept.
    /// </summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The memory cache key of the index over a set of kinds, so a page that needs only albums does not
    /// share (or evict) the index a movie page built.
    /// </summary>
    /// <param name="kinds">The kinds the index covers.</param>
    /// <returns>The key, the same for the same kinds in any order.</returns>
    public static string KeyFor(IEnumerable<BaseItemKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);

        return Key + ":" + string.Join(",", kinds.Distinct().OrderBy(k => k));
    }
}
