using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;

/// <summary>
/// Turns a reader's OpenLibrary "Want to Read" shelf into discovery
/// (<see cref="GapPattern.Recommendation"/>) gaps for the books the library does not own. The shelf already
/// says what the reader wants, so unlike the bibliography source there is no author walk: each entry keys on
/// its own work id.
/// </summary>
internal static class OpenLibraryWantToReadMapper
{
    /// <summary>
    /// Builds gaps for the unowned works on a shelf, de-duplicated by work id and capped.
    /// </summary>
    /// <param name="username">The OpenLibrary username the shelf belongs to.</param>
    /// <param name="works">The works on the shelf.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="maxResults">The most gaps to emit.</param>
    /// <returns>The discovery gaps for unowned works.</returns>
    public static IEnumerable<GapItem> Build(
        string username,
        IEnumerable<OpenLibraryReadingLogWork> works,
        OwnershipIndex ownership,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(works);
        ArgumentNullException.ThrowIfNull(ownership);

        var emitted = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ownerId = GapSourceKeys.OpenLibraryWantToRead.Owner(username);

        foreach (var work in works)
        {
            if (emitted >= maxResults)
            {
                break;
            }

            var workId = OpenLibraryMapper.NormalizeWorkKey(work.Key);
            var title = work.Title;
            if (string.IsNullOrEmpty(workId) || string.IsNullOrEmpty(title) || !seen.Add(workId))
            {
                continue;
            }

            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderIds.OpenLibrary] = workId
            };

            if (ownership.OwnsAny(BaseItemKind.Book, providerIds))
            {
                continue;
            }

            // The shelf names the authors inline, so the row can say who wrote it without a second lookup.
            var author = work.AuthorNames is { Count: > 0 } authors ? authors[0] : null;

            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"{GapSourceKeys.OpenLibraryWantToRead.GapPrefix}{username}:{workId}"),
                pattern: GapPattern.Recommendation,
                domain: MediaDomain.Books,
                targetKind: BaseItemKind.Book,
                name: title,
                providerIds: providerIds,
                sourceItemId: ownerId,
                sourceItemName: "OpenLibrary want to read",
                sourceItemType: SourceItemTypes.OpenLibraryShelf,
                releaseDate: work.FirstPublishYear is > 0
                    ? new DateTime(work.FirstPublishYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    : null,
                imageUrl: OpenLibraryClient.CoverUrl(work.CoverId),
                overview: author);
        }
    }
}
