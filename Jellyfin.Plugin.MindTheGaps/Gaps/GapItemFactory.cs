using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Text;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Builds <see cref="GapItem"/>s from the fields common to every source, so sources don't repeat
/// the boilerplate (year/upcoming derivation, link building, source-item wiring).
/// </summary>
internal static class GapItemFactory
{
    /// <summary>
    /// Creates a gap item, deriving <see cref="GapItem.Year"/>/<see cref="GapItem.IsUpcoming"/> from
    /// <paramref name="releaseDate"/> (and, for an undated title, <paramref name="domain"/>) and
    /// building links from <paramref name="providerIds"/>.
    /// </summary>
    /// <param name="id">Stable de-dup id.</param>
    /// <param name="pattern">The gap pattern.</param>
    /// <param name="domain">The media domain.</param>
    /// <param name="targetKind">What the missing thing is (BaseItemKind.Movie, Series, Episode, ...).</param>
    /// <param name="name">The title.</param>
    /// <param name="providerIds">The candidate's provider ids.</param>
    /// <param name="sourceItemId">Id (N-format guid) of the owned item that surfaced this gap.</param>
    /// <param name="sourceItemName">Name of the owned item that surfaced this gap.</param>
    /// <param name="sourceItemType">The owning item's type label.</param>
    /// <param name="sourceProviderIds">The source/creator's own provider ids, for building links to its page.</param>
    /// <param name="releaseDate">The release date, if known.</param>
    /// <param name="imageUrl">A poster/image URL.</param>
    /// <param name="overview">A short overview / role description.</param>
    /// <param name="season">The season number for episode gaps (0 is specials).</param>
    /// <param name="extraLinks">Links to append beyond those derived from <paramref name="providerIds"/>.</param>
    /// <param name="sourceItemYear">The release year of the owning item, if known.</param>
    /// <param name="setOwnedCount">For a counted set, how many members the library already owns.</param>
    /// <param name="setTotalCount">For a counted set, the total number of members.</param>
    /// <param name="sortScore">An optional popularity score for the report's sort-by-popularity.</param>
    /// <returns>The constructed gap item.</returns>
    public static GapItem Create(
        string id,
        GapPattern pattern,
        MediaDomain domain,
        BaseItemKind targetKind,
        string name,
        IReadOnlyDictionary<string, string> providerIds,
        string sourceItemId,
        string? sourceItemName,
        string sourceItemType,
        IReadOnlyDictionary<string, string>? sourceProviderIds = null,
        DateTime? releaseDate = null,
        string? imageUrl = null,
        string? overview = null,
        int? season = null,
        IEnumerable<ExternalLink>? extraLinks = null,
        int? sourceItemYear = null,
        int? setOwnedCount = null,
        int? setTotalCount = null,
        double? sortScore = null)
    {
        var links = ProviderLinks.Build(targetKind, providerIds);
        if (extraLinks is not null)
        {
            var combined = new List<ExternalLink>(links);
            combined.AddRange(extraLinks);
            links = combined;
        }

        return new GapItem
        {
            Id = id,
            Pattern = pattern,
            Domain = domain,
            TargetKind = targetKind,
            Name = name,
            Year = releaseDate?.Year,
            Season = season,
            ReleaseDate = releaseDate,
            IsUpcoming = NotYetReleased(domain, releaseDate),
            ImageUrl = imageUrl,
            Overview = HtmlText.ToPlainText(overview),
            ProviderIds = providerIds,
            Links = links,
            SourceItemId = sourceItemId,
            SourceItemName = sourceItemName,
            SourceItemType = sourceItemType,
            SourceLinks = CreatorLinks.Build(sourceItemType, sourceProviderIds),
            SourceItemYear = sourceItemYear,
            SetOwnedCount = setOwnedCount,
            SetTotalCount = setTotalCount,
            SortScore = sortScore
        };
    }

    /// <summary>
    /// Re-derives <see cref="GapItem.IsUpcoming"/> across a set of gaps, for a scan to run over the report it
    /// is about to save. The accumulate passes carry prior <see cref="GapItem"/> objects forward untouched, so
    /// without this a gap keeps whatever the scan that first found it decided, and "upcoming" is relative to
    /// today rather than fixed. Cheap (no I/O) and idempotent, so it runs over everything rather than trying
    /// to work out which items were carried.
    /// </summary>
    /// <param name="gaps">The gaps to refresh in place.</param>
    public static void RefreshUpcoming(IEnumerable<GapItem> gaps)
    {
        ArgumentNullException.ThrowIfNull(gaps);

        foreach (var gap in gaps)
        {
            gap.IsUpcoming = NotYetReleased(gap.Domain, gap.ReleaseDate);
        }
    }

    /// <summary>
    /// Determines whether a title is not out yet: a known future date, or, for a movie/series/episode, no
    /// date at all. TMDB and the episode providers carry a date on anything that has actually come out, so
    /// an undated title from them is announced but unscheduled (a "Bond 26", a "Mad Max: Wasteland"). Music
    /// and books are the opposite: Discogs, MusicBrainz, and OpenLibrary routinely have no date on a release
    /// from decades ago, so an undated one there stays reported as missing.
    /// </summary>
    /// <remarks>
    /// The answer depends on today's date, so it is not a property of the gap and cannot be computed once
    /// and kept. A scan re-derives it for every item it is about to save, including the ones carried forward
    /// from the previous report (see <see cref="GapEngine"/>), which is what stops a title staying
    /// "upcoming" after its release date passes.
    /// </remarks>
    /// <param name="domain">The media domain, which decides what an absent date means.</param>
    /// <param name="releaseDate">The release date, if known.</param>
    /// <returns><see langword="true"/> when the title is not released yet.</returns>
    public static bool NotYetReleased(MediaDomain domain, DateTime? releaseDate)
        => releaseDate.HasValue
            ? releaseDate.Value.Date > DateTime.UtcNow.Date
            : domain is MediaDomain.Movies or MediaDomain.Shows;
}
