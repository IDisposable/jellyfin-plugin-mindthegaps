using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Imdb;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Imdb;

/// <summary>
/// Turns the titles on an IMDb list or watchlist into discovery (<see cref="GapPattern.Recommendation"/>)
/// gaps for the ones the library does not own. Every entry carries its IMDb id, so the gap keys on that
/// directly and the ownership diff and link building work unchanged. The entry's kind routes an episodic
/// title to the Shows domain and everything else to Movies.
/// </summary>
internal static class ImdbListMapper
{
    /// <summary>
    /// Builds gaps for a list's unowned titles, de-duplicated by IMDb id and capped.
    /// </summary>
    /// <param name="listId">The list's "ls" id.</param>
    /// <param name="listName">The list's display name (the gap's source).</param>
    /// <param name="titles">The titles on the list.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="maxResults">The most gaps to emit for this list.</param>
    /// <returns>The discovery gaps for unowned titles.</returns>
    public static IEnumerable<GapItem> Build(
        string listId,
        string? listName,
        IEnumerable<ImdbListEntry> titles,
        OwnershipIndex ownership,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(ownership);

        var emitted = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var title in titles)
        {
            if (emitted >= maxResults)
            {
                break;
            }

            // A list can also hold people and images; those deserialize with no id and are not gaps.
            var imdbId = title.Id;
            var name = title.TitleText?.Value;
            if (string.IsNullOrEmpty(imdbId) || string.IsNullOrEmpty(name) || !seen.Add(imdbId))
            {
                continue;
            }

            var isSeries = title.TitleType?.CanHaveEpisodes == true;
            var kind = isSeries ? BaseItemKind.Series : BaseItemKind.Movie;
            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderIds.Imdb] = imdbId
            };

            if (ownership.OwnsAny(kind, providerIds))
            {
                continue;
            }

            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"{GapIdPrefixes.ImdbList}{listId}:{imdbId}"),
                pattern: GapPattern.Recommendation,
                domain: isSeries ? MediaDomain.Shows : MediaDomain.Movies,
                targetKind: kind,
                name: name,
                providerIds: providerIds,
                sourceItemId: SourceItemIds.ImdbList(listId),
                sourceItemName: listName,
                sourceItemType: SourceItemTypes.ImdbList,
                releaseDate: title.ReleaseYear?.Year is > 0
                    ? new DateTime(title.ReleaseYear.Year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    : null,
                imageUrl: title.PrimaryImage?.Url);
        }
    }
}
