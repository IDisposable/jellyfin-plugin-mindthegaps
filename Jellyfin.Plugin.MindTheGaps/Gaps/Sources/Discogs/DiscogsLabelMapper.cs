using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;

/// <summary>
/// Turns a Discogs label's releases into <see cref="GapPattern.SetCompletion"/> gaps for the releases the
/// library does not own. Ownership is checked by Discogs release id first, then by a conservative artist-and-
/// title name match (<see cref="OwnershipIndex.OwnsByName"/>), so a release the library holds under a
/// MusicBrainz id rather than a Discogs one is still recognized as owned. Pure and host-free so the
/// captured-fixture tests can exercise it directly.
/// </summary>
internal static class DiscogsLabelMapper
{
    /// <summary>
    /// Builds gaps for a label's unowned releases, de-duplicated by Discogs id and capped so one prolific
    /// label does not flood the list.
    /// </summary>
    /// <param name="labelId">The Discogs label id (used in the stable gap id and the synthetic source id).</param>
    /// <param name="labelName">The label's display name (the set name).</param>
    /// <param name="releases">The label's releases from Discogs.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="maxResults">The maximum number of gaps to emit for this label.</param>
    /// <returns>The gaps for the unowned releases.</returns>
    public static IEnumerable<GapItem> Build(
        long labelId,
        string? labelName,
        IEnumerable<DiscogsRelease> releases,
        OwnershipIndex ownership,
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

            if (release.Id <= 0 || string.IsNullOrEmpty(release.Title) || !seen.Add(release.Id))
            {
                continue;
            }

            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderIds.Discogs] = release.Id.ToString(CultureInfo.InvariantCulture)
            };

            if (ownership.OwnsAny(BaseItemKind.MusicAlbum, providerIds)
                || ownership.OwnsByName(BaseItemKind.MusicAlbum, release.Artist, release.Title))
            {
                continue;
            }

            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"discogslabel:{labelId}:{release.Id}"),
                pattern: GapPattern.SetCompletion,
                domain: MediaDomain.Music,
                targetKind: BaseItemKind.MusicAlbum,
                name: DisplayName(release),
                providerIds: providerIds,
                sourceItemId: string.Create(CultureInfo.InvariantCulture, $"discogs-label-{labelId}"),
                sourceItemName: labelName,
                sourceItemType: "MusicLabel",
                sourceProviderIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Discogs] = labelId.ToString(CultureInfo.InvariantCulture) },
                releaseDate: release.Year > 0 ? new DateTime(release.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null);
        }
    }

    // Prefix the title with the artist when one is given, so a release reads "Artist - Title".
    private static string DisplayName(DiscogsRelease release)
        => string.IsNullOrEmpty(release.Artist)
            ? release.Title!
            : string.Create(CultureInfo.InvariantCulture, $"{release.Artist} - {release.Title}");
}
