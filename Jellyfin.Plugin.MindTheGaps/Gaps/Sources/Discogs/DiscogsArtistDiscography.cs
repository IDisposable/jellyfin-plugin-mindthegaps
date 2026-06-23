using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;

/// <summary>
/// Shared helpers for scanning an artist's Discogs discography: resolving the artist's Discogs id (used by
/// both the standalone Discogs artist source and the MusicBrainz completeness pass), and excluding the
/// releases a MusicBrainz album list already covers so completeness only adds what MusicBrainz misses.
/// </summary>
internal static class DiscogsArtistDiscography
{
    /// <summary>
    /// Resolves an owned artist's Discogs id: a Discogs id already on the item if present, otherwise a
    /// conservative name search. Null when the item has no Discogs id and the name resolves to nothing (or
    /// the artist has no name).
    /// </summary>
    /// <param name="artist">The owned library artist.</param>
    /// <param name="discogs">The Discogs client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The Discogs artist id, or null.</returns>
    public static async Task<long?> ResolveIdAsync(BaseItem artist, DiscogsClient discogs, CancellationToken cancellationToken)
    {
        // DiscogsProvider is the provider-id key an item carries (distinct from the HTTP service name).
        if (artist.TryGetProviderId(DiscogsLabelMapper.DiscogsProvider, out var tagged)
            && long.TryParse(tagged, NumberStyles.Integer, CultureInfo.InvariantCulture, out var taggedId)
            && taggedId > 0)
        {
            return taggedId;
        }

        return string.IsNullOrEmpty(artist.Name)
            ? null
            : await discogs.SearchArtistAsync(artist.Name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the Discogs releases whose normalized title is not among the given titles, so the completeness
    /// pass adds only the albums a MusicBrainz album list misses. Pure, so a test can exercise the de-dup.
    /// </summary>
    /// <param name="releases">The Discogs releases.</param>
    /// <param name="excludeTitles">The titles to exclude (a MusicBrainz album list), normalized the same way.</param>
    /// <returns>The releases not covered by the excluded titles.</returns>
    public static IReadOnlyList<DiscogsRelease> ExcludingTitles(IEnumerable<DiscogsRelease> releases, IEnumerable<string?> excludeTitles)
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var title in excludeTitles)
        {
            excluded.Add(TextKey.Normalize(title));
        }

        return releases.Where(r => !excluded.Contains(TextKey.Normalize(r.Title))).ToList();
    }
}
