using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Recovers the ids a report or list written by an earlier version kept only inside a stored link, before
/// those links are dropped (see <see cref="StoredJson"/>): a JustWatch title's page, and the ids behind a
/// source's links. Every other stored link is one the ids beside it already rebuild. Runs on every load, and
/// does nothing once the ids are there, so a file is converted by the first save after it is read.
/// </summary>
internal static class LegacyLinkIds
{
    private static readonly (string Host, string Segment, string Key)[] _sourcePages =
    [
        ("www.themoviedb.org", string.Empty, ProviderIds.Tmdb),
        ("www.imdb.com", "name", ProviderIds.Imdb),
        ("trakt.tv", "people", ProviderIds.Trakt),
        ("musicbrainz.org", "artist", ProviderIds.MusicBrainzArtist),
        ("openlibrary.org", "authors", ProviderIds.OpenLibrary),
        ("www.discogs.com", "artist", ProviderIds.Discogs),
        ("www.discogs.com", "label", ProviderIds.Discogs)
    ];

    /// <summary>
    /// Moves the ids a gap's stored links carry onto its ids.
    /// </summary>
    /// <param name="gap">The gap, changed in place (it has just been read and nothing else holds it).</param>
    public static void Recover(GapItem gap)
    {
        ArgumentNullException.ThrowIfNull(gap);
        gap.ProviderIds = WithJustWatch(gap.ProviderIds, gap.Links);
        if (gap.SourceProviderIds is null && gap.SourceLinks.Count > 0)
        {
            gap.SourceProviderIds = SourceIds(gap.SourceLinks);
        }
    }

    /// <summary>
    /// Moves the JustWatch page a list entry's stored links carry onto its ids.
    /// </summary>
    /// <param name="entry">The entry, changed in place.</param>
    public static void Recover(TodoEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.ProviderIds = WithJustWatch(entry.ProviderIds, entry.Links);
    }

    // A JustWatch title page ("/us/movie/the-matrix") is what the JustWatch id is, so the stored page becomes the
    // id. A search page is not a title and is skipped.
    private static IReadOnlyDictionary<string, string> WithJustWatch(IReadOnlyDictionary<string, string> ids, IReadOnlyList<ExternalLink> links)
    {
        foreach (var existing in ids.Keys)
        {
            if (string.Equals(existing, ProviderIds.JustWatch, StringComparison.OrdinalIgnoreCase))
            {
                return ids;
            }
        }

        foreach (var link in links)
        {
            if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri)
                && string.Equals(uri.Host, "www.justwatch.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.Length > 1
                && !uri.AbsolutePath.Contains("/search", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, string>(ids, StringComparer.OrdinalIgnoreCase) { [ProviderIds.JustWatch] = uri.AbsolutePath };
            }
        }

        return ids;
    }

    private static Dictionary<string, string>? SourceIds(IReadOnlyList<ExternalLink> links)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var uri))
            {
                continue;
            }

            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length != 2)
            {
                continue;
            }

            foreach (var (host, segment, key) in _sourcePages)
            {
                if (!string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)
                    || (segment.Length > 0 && !string.Equals(segments[0], segment, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // A TMDB page is "{kind}/{id}" or "{kind}/{id}-{slug}"; the id is the leading digits.
                var id = key == ProviderIds.Tmdb ? LeadingDigits(segments[1]) : segments[1];
                if (!string.IsNullOrEmpty(id))
                {
                    ids.TryAdd(key, id);
                }

                break;
            }
        }

        return ids.Count == 0 ? null : ids;
    }

    private static string? LeadingDigits(string value)
    {
        var end = 0;
        while (end < value.Length && char.IsAsciiDigit(value[end]))
        {
            end++;
        }

        return end == 0 ? null : value[..end];
    }
}
