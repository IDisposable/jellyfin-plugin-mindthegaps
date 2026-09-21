using System;

namespace Jellyfin.Plugin.MindTheGaps.Services.Images;

/// <summary>
/// The hosts the image route will fetch from. The route is anonymous (an image tag cannot send the auth
/// header), so this list is what stops it being a way to make the server request an arbitrary address. A
/// domain here covers its subdomains: the Internet Archive serves covers from numbered hosts.
/// </summary>
internal static class ImageHosts
{
    private static readonly string[] Domains =
    [
        "tmdb.org",
        "coverartarchive.org",
        "archive.org",
        "openlibrary.org",
        "justwatch.com",
        "media-amazon.com",
        "ssl-images-amazon.com",
        "discogs.com",
        "thetvdb.com",
        "tvmaze.com",
        "mdblist.com"
    ];

    /// <summary>
    /// Whether the address may be fetched: HTTPS on the default port, no credentials in it, and a listed host.
    /// </summary>
    /// <param name="uri">The address.</param>
    /// <returns><see langword="true"/> when it may be fetched.</returns>
    public static bool IsAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.IdnHost;
        foreach (var domain in Domains)
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
