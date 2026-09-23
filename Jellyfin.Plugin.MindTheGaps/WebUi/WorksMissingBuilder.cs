using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The pure shaping for the artist and book surfaces: a gap becomes a card, and a list drops what the report
/// has dismissed and orders newest first with undated works last.
/// </summary>
internal static class WorksMissingBuilder
{
    /// <summary>
    /// Shapes one gap into a card.
    /// </summary>
    /// <param name="gap">The album or book gap.</param>
    /// <param name="wanted">The identity keys of the titles on the user's want-to-watch list, or null for none.</param>
    /// <param name="searchUrlTemplate">The configured web-search URL template ("{0}" replaced by the encoded
    /// term), or null to omit the web-search link. An Amazon search is always added: unlike a movie or
    /// series, an album or book carries no TMDB id to browse a page for, so these are its only always-on
    /// links.</param>
    /// <returns>The card.</returns>
    public static MissingWork ToWork(GapItem gap, IReadOnlySet<string>? wanted = null, string? searchUrlTemplate = null)
    {
        ArgumentNullException.ThrowIfNull(gap);

        var searchLinks = BuildSearchLinks(gap.Name, gap.Year, gap.SourceItemName, searchUrlTemplate);
        var links = searchLinks.Count > 0 ? gap.Links.Concat(searchLinks).ToList() : gap.Links;

        return new MissingWork
        {
            GapId = gap.Id,
            Title = gap.Name,
            Year = gap.Year,
            ReleaseDate = gap.ReleaseDate,
            Kind = gap.TargetKind == BaseItemKind.Book ? "Book" : "MusicAlbum",
            Creator = gap.SourceItemName,
            ImageUrl = gap.ImageUrl,
            Upcoming = gap.IsUpcoming,
            OnList = wanted is not null && GapTargetKey.For(gap).Any(wanted.Contains),
            Links = links
        };
    }

    /// <summary>
    /// Builds the card list: only albums and books, minus dismissed gaps, newest first, ties by title.
    /// </summary>
    /// <param name="gaps">The gaps the artist's or author's sources produced.</param>
    /// <param name="dismissed">Whether a gap id has been dismissed on the report.</param>
    /// <param name="wanted">The identity keys of the titles on the user's want-to-watch list, or null for none.</param>
    /// <param name="searchUrlTemplate">The configured web-search URL template, or null to omit it.</param>
    /// <returns>The cards.</returns>
    public static IReadOnlyList<MissingWork> Build(
        IEnumerable<GapItem> gaps,
        Func<string, bool> dismissed,
        IReadOnlySet<string>? wanted = null,
        string? searchUrlTemplate = null)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(dismissed);

        return gaps
            .Where(g => (g.TargetKind is BaseItemKind.MusicAlbum or BaseItemKind.Book) && !dismissed(g.Id))
            .Select(g => ToWork(g, wanted, searchUrlTemplate))
            .OrderByDescending(w => w.ReleaseDate ?? DateTime.MinValue)
            .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // The author/artist is folded into the search term (not just the title) because a book or album title
    // alone is often shared across unrelated works, which is what made the fulfillment queue's own Amazon
    // link (see mindthegaps.report.todo.js's todoSearchTerm) useless without it.
    private static IReadOnlyList<ExternalLink> BuildSearchLinks(string name, int? year, string? creator, string? searchUrlTemplate)
    {
        if (string.IsNullOrEmpty(name))
        {
            return [];
        }

        var term = string.Join(
            ' ',
            new[] { name, year?.ToString(CultureInfo.InvariantCulture), creator }.Where(part => !string.IsNullOrEmpty(part)));
        var encoded = Uri.EscapeDataString(term);

        var links = new List<ExternalLink> { new("Amazon", "https://www.amazon.com/s?k=" + encoded) };
        if (!string.IsNullOrEmpty(searchUrlTemplate))
        {
            links.Add(new ExternalLink("Web search", searchUrlTemplate.Replace("{0}", encoded, StringComparison.Ordinal)));
        }

        return links;
    }
}
