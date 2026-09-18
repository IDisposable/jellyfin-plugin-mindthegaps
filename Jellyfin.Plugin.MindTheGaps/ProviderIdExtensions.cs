using System;
using System.Collections.Generic;
using System.Globalization;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MindTheGaps;

// Extensions for reading provider ids off an item or a gap. Lives in the root namespace so it is in scope
// everywhere without a using.
internal static class ProviderIdExtensions
{
    // The item's id for a provider, or null when absent or blank (a blank value reads as absent, unlike the
    // core accessor). Keys come from ProviderIds.
    internal static string? ProviderIdOrNull(this IHasProviderIds item, string provider)
        => item.TryGetProviderId(provider, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    // Every provider id this plugin parses (TMDB, TheTVDB, TVmaze, Discogs, ...) is a positive integer; a
    // blank, non-numeric, zero, or negative value all read as "no id", the same as if the provider id were
    // absent. Most are comfortably int-sized; TheTVDB and Discogs clients type theirs as long (matching
    // their own API surfaces), hence the long overloads alongside these.
    internal static bool TryParseProviderId(this string? value, out int id)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0;

    internal static bool TryParseProviderIdAsLong(this string? value, out long id)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0;

    // The numeric form of ProviderIdOrNull, for a live item (a library BaseItem, a TMDbLib person/title, ...).
    internal static bool TryGetProviderIdAsInt(this IHasProviderIds item, string provider, out int id)
        => item.ProviderIdOrNull(provider).TryParseProviderId(out id);

    internal static bool TryGetProviderIdAsLong(this IHasProviderIds item, string provider, out long id)
        => item.ProviderIdOrNull(provider).TryParseProviderIdAsLong(out id);

    // The numeric form for a gap's persisted ProviderIds dictionary. Walks case-insensitively rather than
    // calling TryGetValue directly, since the dictionary's own comparer is whatever it happened to be built
    // or deserialized with, and this is the one place that answer needs to not matter.
    internal static bool TryGetProviderIdAsInt(this IReadOnlyDictionary<string, string> providerIds, string provider, out int id)
    {
        foreach (var pair in providerIds)
        {
            if (string.Equals(pair.Key, provider, StringComparison.OrdinalIgnoreCase) && pair.Value.TryParseProviderId(out id))
            {
                return true;
            }
        }

        id = 0;
        return false;
    }
}
