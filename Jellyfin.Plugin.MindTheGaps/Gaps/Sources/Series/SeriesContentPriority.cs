using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Resolves a Shows library's metadata fetcher order to <see cref="KnownProvider"/> entries and ranks a
/// provider against it, so the series-content merge claims each season for the provider the user prefers.
/// The user's top fetcher ranks first; a provider the library does not list ranks below the listed ones; a
/// source that is not a Jellyfin metadata fetcher (TVmaze, <see cref="KnownProvider"/> null) ranks last.
/// </summary>
internal static class SeriesContentPriority
{
    /// <summary>
    /// The Shows library's metadata fetcher order for the series, resolved to known providers. An unknown
    /// fetcher becomes null and keeps its position, so the ranking of the providers the plugin knows stays
    /// correct. Empty when the library has no configured order.
    /// </summary>
    /// <param name="series">The owned series, for its library options.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <returns>The fetcher order as known providers (an unknown fetcher is null).</returns>
    public static IReadOnlyList<KnownProvider?> FetcherOrder(BaseItem series, ILibraryManager libraryManager)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(libraryManager);

        var options = libraryManager.GetLibraryOptions(series);
        if (options?.TypeOptions is null)
        {
            return [];
        }

        foreach (var typeOption in options.TypeOptions)
        {
            if (string.Equals(typeOption.Type, "Series", StringComparison.Ordinal) && typeOption.MetadataFetcherOrder is { Length: > 0 })
            {
                return typeOption.MetadataFetcherOrder.Select(name => KnownProviders.ForName(name)).ToList();
            }
        }

        return [];
    }

    /// <summary>
    /// Ranks a provider against the fetcher order: its position when the library lists it, after the listed
    /// providers when it is a known provider the library does not list, and last when it is not a Jellyfin
    /// metadata fetcher at all (a null provider, TVmaze). Lower ranks claim a season first in the merge.
    /// </summary>
    /// <param name="order">The library's fetcher order, resolved to known providers.</param>
    /// <param name="provider">The provider to rank, or null for a non-fetcher source.</param>
    /// <returns>The rank, lowest first.</returns>
    public static int Rank(IReadOnlyList<KnownProvider?> order, KnownProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (provider is null)
        {
            return int.MaxValue;
        }

        for (var i = 0; i < order.Count; i++)
        {
            if (ReferenceEquals(order[i], provider))
            {
                return i;
            }
        }

        return order.Count;
    }
}
