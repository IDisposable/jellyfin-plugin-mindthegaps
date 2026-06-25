using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;

/// <summary>
/// Turns the works tagged with an OpenLibrary subject into <see cref="GapPattern.SetCompletion"/> gaps for
/// the books the library does not own. Ownership is checked by OpenLibrary work id first, then by a
/// conservative author-and-title name match (<see cref="OwnershipIndex.OwnsByName"/>), so a book the library
/// holds under a different work key is still recognized as owned. Pure and host-free so the fixture tests
/// exercise it directly.
/// </summary>
internal static class OpenLibrarySubjectMapper
{
    /// <summary>
    /// Builds gaps for a subject's unowned works, de-duplicated by OpenLibrary work id and capped so one broad
    /// subject does not flood the list.
    /// </summary>
    /// <param name="subject">The subject slug (used in the stable gap id and the synthetic source id).</param>
    /// <param name="subjectName">The subject's display name (the set name).</param>
    /// <param name="works">The subject's works from OpenLibrary.</param>
    /// <param name="ownership">The library ownership index.</param>
    /// <param name="maxResults">The maximum number of gaps to emit for this subject.</param>
    /// <returns>The gaps for the unowned works.</returns>
    public static IEnumerable<GapItem> Build(
        string subject,
        string? subjectName,
        IEnumerable<OpenLibrarySubjectWork> works,
        OwnershipIndex ownership,
        int maxResults)
    {
        var emitted = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var work in works)
        {
            if (emitted >= maxResults)
            {
                break;
            }

            var workId = OpenLibraryMapper.NormalizeWorkKey(work.Key);
            if (string.IsNullOrEmpty(workId) || string.IsNullOrEmpty(work.Title) || !seen.Add(workId))
            {
                continue;
            }

            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderIds.OpenLibrary] = workId
            };

            var author = FirstAuthorName(work);
            if (ownership.OwnsAny(BaseItemKind.Book, providerIds)
                || ownership.OwnsByName(BaseItemKind.Book, author, work.Title))
            {
                continue;
            }

            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"openlibrarysubject:{subject}:{workId}"),
                pattern: GapPattern.SetCompletion,
                domain: MediaDomain.Books,
                targetKind: BaseItemKind.Book,
                name: work.Title!,
                providerIds: providerIds,
                sourceItemId: string.Create(CultureInfo.InvariantCulture, $"openlibrary-subject-{subject}"),
                sourceItemName: string.IsNullOrEmpty(subjectName) ? subject : subjectName,
                sourceItemType: "Subject",
                releaseDate: work.FirstPublishYear is > 0 and <= 9999
                    ? new DateTime(work.FirstPublishYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    : null);
        }
    }

    // The first named author on a subject work, used for the conservative owned-by-name fallback.
    private static string? FirstAuthorName(OpenLibrarySubjectWork work)
    {
        if (work.Authors is null)
        {
            return null;
        }

        foreach (var author in work.Authors)
        {
            if (!string.IsNullOrEmpty(author.Name))
            {
                return author.Name;
            }
        }

        return null;
    }
}
