namespace Jellyfin.Plugin.MindTheGaps.Providers;

/// <summary>
/// The provider id keys this plugin registers an external id and url provider for, because the ids drive its
/// own sources and core ships no provider for them. Each steps aside when another plugin provides the same key
/// (see <see cref="ProviderPrecedence"/>).
/// </summary>
internal static class OwnProviders
{
    /// <summary>
    /// Gets the keys.
    /// </summary>
    public static readonly string[] Keys = [ProviderIds.OpenLibrary, ProviderIds.Discogs];
}
