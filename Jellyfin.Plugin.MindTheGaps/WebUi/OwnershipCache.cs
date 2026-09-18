using System;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The one cache entry the web UI services share for the owned movies-and-series index: one library read,
/// kept briefly so browsing several pages in a row does not re-read the library each time, while a title
/// added to the library still drops off within a minute.
/// </summary>
internal static class OwnershipCache
{
    /// <summary>
    /// The memory cache key.
    /// </summary>
    public const string Key = "mtg-webui-ownership";

    /// <summary>
    /// How long the index is kept.
    /// </summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
}
