using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MindTheGaps;

// Extensions for reading provider ids off an item. Lives in the root namespace so it is in scope everywhere
// without a using.
internal static class ProviderIdExtensions
{
    // The item's id for a provider, or null when absent or blank (a blank value reads as absent, unlike the
    // core accessor). Keys come from ProviderIds.
    internal static string? ProviderIdOrNull(this IHasProviderIds item, string provider)
        => item.TryGetProviderId(provider, out var value) && !string.IsNullOrEmpty(value) ? value : null;
}
