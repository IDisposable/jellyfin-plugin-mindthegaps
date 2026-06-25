using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services;
using Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;

/// <summary>
/// Turns an author's OpenLibrary works into <see cref="GapPattern.CreatorWorks"/> gaps for the books
/// the library does not already own. Pure and host-free so the captured-fixture tests exercise it.
/// </summary>
internal static class OpenLibraryMapper
{
    /// <summary>
    /// Builds bibliography gaps for an author's unowned works, capped to keep one prolific author from
    /// flooding the list.
    /// </summary>
    /// <param name="authorKey">The OpenLibrary author key (used in the stable gap id).</param>
    /// <param name="authorName">The author's display name (used as the source-item name).</param>
    /// <param name="works">The author's works from OpenLibrary.</param>
    /// <param name="sourceItemId">The owned library book's id (N-format guid) that surfaced this author.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="maxResults">The maximum number of gaps to emit for this author.</param>
    /// <returns>The bibliography gaps for the unowned works.</returns>
    public static IEnumerable<GapItem> Build(
        string authorKey,
        string? authorName,
        IEnumerable<OpenLibraryWork> works,
        string sourceItemId,
        OwnershipIndex ownership,
        int maxResults)
    {
        var emitted = 0;

        // De-duplicate works that share a title (OpenLibrary lists reissues and translations as distinct
        // works), keeping one gap per title. The representative is the earliest-published edition, and a
        // title counts as owned when any of its works is owned, so a title held under a different work key is
        // not reported missing.
        var groups = works
            .Where(w => !string.IsNullOrEmpty(NormalizeWorkKey(w.Key)) && !string.IsNullOrEmpty(w.Title))
            .GroupBy(w => TextKey.Normalize(w.Title), StringComparer.Ordinal)
            .Where(g => g.Key.Length > 0);

        foreach (var group in groups)
        {
            if (emitted >= maxResults)
            {
                break;
            }

            if (group.Any(w => ownership.OwnsAny(BaseItemKind.Book, ProviderIdsFor(w))))
            {
                continue;
            }

            var work = group.OrderBy(w => YearOf(w) ?? int.MaxValue).First();
            var workId = NormalizeWorkKey(work.Key)!;

            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"bibliography:{authorKey}:{workId}"),
                pattern: GapPattern.CreatorWorks,
                domain: MediaDomain.Books,
                targetKind: BaseItemKind.Book,
                name: work.Title!,
                providerIds: ProviderIdsFor(work),
                sourceItemId: sourceItemId,
                sourceItemName: authorName,
                sourceItemType: "Book",
                sourceProviderIds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.OpenLibrary] = authorKey },
                releaseDate: ParseDate(work.FirstPublishDate));
        }
    }

    // An owned-lookup provider map for a work (its OpenLibrary work id under the OpenLibrary provider).
    private static Dictionary<string, string> ProviderIdsFor(OpenLibraryWork work)
        => new(StringComparer.OrdinalIgnoreCase) { [ProviderIds.OpenLibrary] = NormalizeWorkKey(work.Key)! };

    // The work's first publish year, when its date parses to one.
    private static int? YearOf(OpenLibraryWork work) => ParseDate(work.FirstPublishDate)?.Year;

    /// <summary>
    /// Strips the "/works/" prefix OpenLibrary returns so the stored key matches the bare work id a
    /// metadata plugin records.
    /// </summary>
    /// <param name="key">The raw work key.</param>
    /// <returns>The bare work id, or null/empty when absent.</returns>
    public static string? NormalizeWorkKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }

        var slash = key.LastIndexOf('/');
        return slash >= 0 && slash < key.Length - 1 ? key.Substring(slash + 1) : key;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var full))
        {
            return full;
        }

        // OpenLibrary first_publish_date is free-form; pull a 4-digit year if there is one. The range is
        // the DateTime-representable one (1 to 9999 CE), so old works are kept, not cut at year 1000. BCE
        // dates cannot be represented by DateTime at all, so an ancient work simply gets no date here.
        for (var i = 0; i + 4 <= value.Length; i++)
        {
            var slice = value.Substring(i, 4);
            if (int.TryParse(slice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                && year is >= 1 and <= 9999)
            {
                return new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }
        }

        return null;
    }
}
