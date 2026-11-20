using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.ListenBrainz;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;

/// <summary>
/// Turns the artists ListenBrainz finds similar to an owned artist into discovery
/// (<see cref="GapPattern.Recommendation"/>) gaps for the ones the library does not own. The gap id is the
/// similar artist's MusicBrainz id alone, so several owned artists that suggest the same one fold onto one gap.
/// </summary>
internal static class MusicRecommendationMapper
{
    /// <summary>
    /// Builds gaps for the unowned similar artists, closest first, capped.
    /// </summary>
    /// <param name="similar">The similar artists, as ListenBrainz ranks them.</param>
    /// <param name="sourceItemId">The owned artist's id (N format), the gaps' source.</param>
    /// <param name="sourceItemName">The owned artist's name.</param>
    /// <param name="sourceMbid">The owned artist's MusicBrainz id, which the gaps' source links are built from.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="maxResults">The most gaps to emit for this artist.</param>
    /// <returns>The discovery gaps.</returns>
    public static IEnumerable<GapItem> Build(
        IEnumerable<ListenBrainzSimilarArtist> similar,
        string sourceItemId,
        string? sourceItemName,
        string sourceMbid,
        OwnershipIndex ownership,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(similar);
        ArgumentNullException.ThrowIfNull(ownership);

        var sourceProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderIds.MusicBrainzArtist] = sourceMbid
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sourceMbid };
        var emitted = 0;
        foreach (var artist in similar)
        {
            if (emitted >= maxResults)
            {
                yield break;
            }

            var mbid = artist.ArtistMbid?.Trim();
            if (string.IsNullOrEmpty(mbid) || string.IsNullOrWhiteSpace(artist.Name) || !seen.Add(mbid))
            {
                continue;
            }

            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderIds.MusicBrainzArtist] = mbid
            };
            if (ownership.OwnsAny(BaseItemKind.MusicArtist, providerIds))
            {
                continue;
            }

            emitted++;
            yield return GapItemFactory.Create(
                id: GapSourceKeys.RecommendationArtist.Gap(mbid.ToLowerInvariant()),
                pattern: GapPattern.Recommendation,
                domain: MediaDomain.Music,
                targetKind: BaseItemKind.MusicArtist,
                name: artist.Name.Trim(),
                providerIds: providerIds,
                sourceItemId: sourceItemId,
                sourceItemName: sourceItemName,
                sourceItemType: SourceItemTypes.MusicArtist,
                sourceProviderIds: sourceProviderIds,
                overview: string.IsNullOrWhiteSpace(artist.Comment) ? null : artist.Comment.Trim(),
                sortScore: artist.Score);
        }
    }
}
