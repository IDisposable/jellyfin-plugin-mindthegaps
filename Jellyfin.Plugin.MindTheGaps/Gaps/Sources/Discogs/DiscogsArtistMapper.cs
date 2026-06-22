using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;

/// <summary>
/// Turns a Discogs artist's releases into gaps for the albums the library does not own. Filters to the
/// artist's own master releases (one entry per album, not every pressing, and not guest appearances), so it
/// complements the MusicBrainz sources rather than flooding the list. Ownership is checked by Discogs id
/// first, then the conservative artist-and-title name match, so an album held under a MusicBrainz id is still
/// recognised. Pure and host-free so a fixture exercises it directly.
/// </summary>
public static class DiscogsArtistMapper
{
    /// <summary>
    /// Builds gaps for an artist's unowned master albums, de-duplicated by Discogs id and capped so one
    /// prolific artist does not flood the list.
    /// </summary>
    /// <param name="artistId">The Discogs artist id (used in the stable gap id).</param>
    /// <param name="artistName">The owned library artist's name (the gap's set name).</param>
    /// <param name="releases">The artist's releases from Discogs.</param>
    /// <param name="sourceItemId">The owned library artist's id (N-format guid).</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="pattern">The gap pattern (discography set vs artist works).</param>
    /// <param name="maxResults">The maximum number of gaps to emit for this artist.</param>
    /// <returns>The gaps for the unowned albums.</returns>
    public static IEnumerable<GapItem> Build(
        long artistId,
        string? artistName,
        IEnumerable<DiscogsRelease> releases,
        string sourceItemId,
        OwnershipIndex ownership,
        GapPattern pattern,
        int maxResults)
    {
        var emitted = 0;
        var seen = new HashSet<long>();
        foreach (var release in releases)
        {
            if (emitted >= maxResults)
            {
                break;
            }

            // One entry per album (a master) and only the artist's own main works (not guest appearances).
            if (!string.Equals(release.Type, "master", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(release.Role, "Main", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (release.Id <= 0 || string.IsNullOrEmpty(release.Title) || !seen.Add(release.Id))
            {
                continue;
            }

            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DiscogsLabelMapper.DiscogsProvider] = release.Id.ToString(CultureInfo.InvariantCulture)
            };

            // Match against the owned artist's name (the library's canonical spelling), not the Discogs
            // credit (which may carry a "(2)" disambiguator), so the title name match lines up.
            if (ownership.OwnsAny(BaseItemKind.MusicAlbum, providerIds)
                || ownership.OwnsByName(BaseItemKind.MusicAlbum, artistName, release.Title))
            {
                continue;
            }

            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"discogsartist:{artistId}:{release.Id}"),
                pattern: pattern,
                domain: MediaDomain.Music,
                targetKind: BaseItemKind.MusicAlbum,
                name: release.Title!,
                providerIds: providerIds,
                sourceItemId: sourceItemId,
                sourceItemName: artistName,
                sourceItemType: "MusicArtist",
                releaseDate: release.Year > 0 ? new DateTime(release.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null);
        }
    }
}
