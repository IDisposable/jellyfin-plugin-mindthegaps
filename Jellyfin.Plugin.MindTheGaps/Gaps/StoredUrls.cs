using System;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Shortens an image or offer address on a host the plugin knows to a token for storage, and expands it back.
/// A report holds thousands of these, and the host part of each is the same string every time. An address on
/// any other host is stored as it is, and a stored address that is not a token reads back unchanged, so a
/// file written before this reads the same.
/// </summary>
internal static class StoredUrls
{
    // A token starts with a character no stored address starts with, so telling the two apart needs no
    // marker beyond it. The tokens are a storage format: one is never renamed or reused for another host.
    private static readonly (string Token, string Prefix)[] _table =
    [
        ("~tmdbimg/", "https://image.tmdb.org/t/p/"),
        ("~tmdb/", "https://www.themoviedb.org/"),
        ("~olcover/", "https://covers.openlibrary.org/b/id/"),
        ("~caa/", "https://coverartarchive.org/release-group/"),
        ("~jwimg/", "https://images.justwatch.com/"),
        ("~tvdbimg/", "https://artworks.thetvdb.com/"),
        ("~tvmazeimg/", "https://static.tvmaze.com/"),
        ("~imdbimg/", "https://m.media-amazon.com/images/"),
        ("~discogsimg/", "https://i.discogs.com/")
    ];

    /// <summary>
    /// Shortens an address for storage.
    /// </summary>
    /// <param name="url">The address.</param>
    /// <returns>The token form, or the address as it is when its host is not one the plugin knows.</returns>
    public static string Compact(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        foreach (var (token, prefix) in _table)
        {
            if (url.StartsWith(prefix, StringComparison.Ordinal))
            {
                return string.Concat(token, url.AsSpan(prefix.Length));
            }
        }

        return url;
    }

    /// <summary>
    /// Expands a stored address.
    /// </summary>
    /// <param name="stored">The stored form.</param>
    /// <returns>The full address, or the stored value as it is when it is not a token.</returns>
    public static string? Expand(string? stored)
    {
        if (stored is null || !stored.StartsWith('~'))
        {
            return stored;
        }

        foreach (var (token, prefix) in _table)
        {
            if (stored.StartsWith(token, StringComparison.Ordinal))
            {
                return string.Concat(prefix, stored.AsSpan(token.Length));
            }
        }

        return stored;
    }
}
