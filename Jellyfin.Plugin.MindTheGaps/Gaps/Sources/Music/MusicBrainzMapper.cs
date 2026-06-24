using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;

/// <summary>
/// Turns an artist's MusicBrainz release-groups into gaps for the studio albums the library does not
/// own. Pure and host-free so the captured-fixture tests can exercise it directly.
/// </summary>
public static class MusicBrainzMapper
{
    /// <summary>
    /// Builds gaps for an artist's unowned studio-album release-groups. The pattern and id prefix tell
    /// the two music sources apart: an album artist you collect yields a <see cref="GapPattern.SetCompletion"/>
    /// "discography" gap, an artist you only own tracks by yields a <see cref="GapPattern.CreatorWorks"/>
    /// "artistworks" gap.
    /// </summary>
    /// <param name="artistMbid">The artist's MusicBrainz id (used in the stable gap id).</param>
    /// <param name="releaseGroups">The artist's album release-groups from MusicBrainz.</param>
    /// <param name="sourceItemId">The owned library artist's id (N-format guid).</param>
    /// <param name="sourceItemName">The owned library artist's name.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="pattern">The gap pattern to tag each gap with.</param>
    /// <param name="idPrefix">The stable-id prefix that distinguishes the two music sources.</param>
    /// <returns>The gaps for the unowned studio albums.</returns>
    public static IEnumerable<GapItem> Build(
        string artistMbid,
        IEnumerable<Services.MusicBrainz.MusicBrainzReleaseGroup> releaseGroups,
        string sourceItemId,
        string? sourceItemName,
        OwnershipIndex ownership,
        GapPattern pattern,
        string idPrefix)
    {
        foreach (var group in releaseGroups)
        {
            if (string.IsNullOrEmpty(group.Id) || string.IsNullOrEmpty(group.Title))
            {
                continue;
            }

            if (!IsStudioAlbum(group))
            {
                continue;
            }

            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderIds.MusicBrainzReleaseGroup] = group.Id
            };

            if (ownership.OwnsAny(BaseItemKind.MusicAlbum, providerIds))
            {
                continue;
            }

            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"{idPrefix}:{artistMbid}:{group.Id}"),
                pattern: pattern,
                domain: MediaDomain.Music,
                targetKind: BaseItemKind.MusicAlbum,
                name: group.Title,
                providerIds: providerIds,
                sourceItemId: sourceItemId,
                sourceItemName: sourceItemName,
                sourceItemType: "MusicArtist",
                sourceProviderIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.MusicBrainzArtist] = artistMbid },
                releaseDate: ParseDate(group.FirstReleaseDate));
        }
    }

    /// <summary>
    /// Determines whether a release-group is an official studio album: primary type Album with no
    /// secondary type (a secondary type marks a compilation, live, soundtrack, remix, and so on).
    /// </summary>
    /// <param name="group">The release-group.</param>
    /// <returns><see langword="true"/> if it is a plain studio album.</returns>
    public static bool IsStudioAlbum(Services.MusicBrainz.MusicBrainzReleaseGroup group)
    {
        if (!string.Equals(group.PrimaryType, "Album", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return group.SecondaryTypes is null || group.SecondaryTypes.Count == 0;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        // MusicBrainz dates can be partial: yyyy, yyyy-MM, or yyyy-MM-dd.
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var full))
        {
            return full;
        }

        if (int.TryParse(value.Length >= 4 ? value.Substring(0, 4) : value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            && year is > 1000 and < 9999)
        {
            return new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        return null;
    }
}
