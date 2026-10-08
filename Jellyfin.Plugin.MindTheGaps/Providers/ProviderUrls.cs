using System;
using System.Globalization;

namespace Jellyfin.Plugin.MindTheGaps.Providers;

/// <summary>
/// The page addresses for the OpenLibrary and Discogs ids, shared by this plugin's registered url providers
/// and the links it builds for gaps, so the two cannot disagree.
/// </summary>
internal static class ProviderUrls
{
    /// <summary>
    /// The OpenLibrary page for an id. The key's last letter says what it names: a work ("OL45804W"), an
    /// edition ("OL7353617M"), or an author ("OL23919A"). A key with none of those is taken as a work, which is
    /// what a book gap carries.
    /// </summary>
    /// <param name="id">The OpenLibrary key.</param>
    /// <returns>The page address, or null for a blank id.</returns>
    public static string? OpenLibrary(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var key = id.Trim();
        var path = char.ToUpperInvariant(key[^1]) switch
        {
            'A' => "authors",
            'M' => "books",
            _ => "works"
        };
        return string.Create(CultureInfo.InvariantCulture, $"https://openlibrary.org/{path}/{key}");
    }

    /// <summary>
    /// The Discogs page for a release, artist, or label id.
    /// </summary>
    /// <param name="kind">"release", "artist", or "label".</param>
    /// <param name="id">The Discogs id.</param>
    /// <returns>The page address, or null for a blank id.</returns>
    public static string? Discogs(string kind, string? id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"https://www.discogs.com/{kind}/{id.Trim()}");

    /// <summary>
    /// Whether a string reads as a positive integer, which every Discogs id is. Keeps a stray value someone
    /// typed into the metadata editor from becoming a broken link.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>True for a positive integer.</returns>
    public static bool IsDiscogsId(string? id)
        => long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0;

    /// <summary>
    /// Whether a string reads as an OpenLibrary key ("OL" then digits then a letter).
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>True for an OpenLibrary key.</returns>
    public static bool IsOpenLibraryKey(string? id)
    {
        if (id is null || id.Length < 4 || !id.StartsWith("OL", StringComparison.OrdinalIgnoreCase) || !char.IsAsciiLetter(id[^1]))
        {
            return false;
        }

        for (var i = 2; i < id.Length - 1; i++)
        {
            if (!char.IsAsciiDigit(id[i]))
            {
                return false;
            }
        }

        return true;
    }
}
