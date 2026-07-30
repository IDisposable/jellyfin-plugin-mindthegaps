using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;

/// <summary>
/// Turns a Discogs wantlist into discovery (<see cref="GapPattern.Recommendation"/>) gaps for the releases
/// the library does not own. Unlike the label and artist sources, which walk a catalog and diff it, the
/// wantlist is already the answer to "what do I want", so each entry maps straight across on its Discogs
/// release id.
/// </summary>
internal static class DiscogsWantlistMapper
{
    /// <summary>
    /// Builds gaps for the unowned entries on a wantlist, de-duplicated by release id and capped.
    /// </summary>
    /// <param name="username">The Discogs username the wantlist belongs to.</param>
    /// <param name="wants">The wantlist entries.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="maxResults">The most gaps to emit.</param>
    /// <returns>The discovery gaps for unowned releases.</returns>
    public static IEnumerable<GapItem> Build(
        string username,
        IEnumerable<DiscogsWant> wants,
        OwnershipIndex ownership,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(wants);
        ArgumentNullException.ThrowIfNull(ownership);

        var emitted = 0;
        var seen = new HashSet<long>();
        var ownerId = SourceItemIds.DiscogsWantlist(username);

        foreach (var want in wants)
        {
            if (emitted >= maxResults)
            {
                break;
            }

            var info = want.BasicInformation;
            var releaseId = info?.Id > 0 ? info.Id : want.Id;
            var title = info?.Title;
            if (releaseId <= 0 || string.IsNullOrEmpty(title) || !seen.Add(releaseId))
            {
                continue;
            }

            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderIds.Discogs] = releaseId.ToString(CultureInfo.InvariantCulture)
            };

            if (ownership.OwnsAny(BaseItemKind.MusicAlbum, providerIds))
            {
                continue;
            }

            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"{GapIdPrefixes.DiscogsWantlist}{username}:{releaseId}"),
                pattern: GapPattern.Recommendation,
                domain: MediaDomain.Music,
                targetKind: BaseItemKind.MusicAlbum,
                name: title,
                providerIds: providerIds,
                sourceItemId: ownerId,
                sourceItemName: "Discogs wantlist",
                sourceItemType: SourceItemTypes.DiscogsWantlist,
                releaseDate: info?.Year is > 0
                    ? new DateTime(info.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    : null,
                imageUrl: info?.CoverImage,
                overview: CreditedArtists(info));
        }
    }

    // The credited artists as one line, so a row says who it is by. Discogs disambiguates duplicate artist
    // names with a numeric suffix ("Rainbow (11)"), which reads as noise on a report row and is stripped.
    private static string? CreditedArtists(DiscogsBasicInformation? info)
    {
        var names = info?.Artists?
            .Select(a => StripDisambiguator(a.Name))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
        return names is { Count: > 0 } ? string.Join(", ", names) : null;
    }

    private static string? StripDisambiguator(string? name)
    {
        if (string.IsNullOrEmpty(name) || !name.EndsWith(')'))
        {
            return name?.Trim();
        }

        var open = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0)
        {
            return name.Trim();
        }

        var inner = name[(open + 2)..^1];
        return inner.Length > 0 && inner.All(char.IsAsciiDigit) ? name[..open].Trim() : name.Trim();
    }
}
