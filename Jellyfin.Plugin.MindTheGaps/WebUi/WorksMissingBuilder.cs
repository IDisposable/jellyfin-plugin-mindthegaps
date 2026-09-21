using System;
using System.Collections.Generic;
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
    /// <returns>The card.</returns>
    public static MissingWork ToWork(GapItem gap, IReadOnlySet<string>? wanted = null)
    {
        ArgumentNullException.ThrowIfNull(gap);

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
            Links = gap.Links
        };
    }

    /// <summary>
    /// Builds the card list: only albums and books, minus dismissed gaps, newest first, ties by title.
    /// </summary>
    /// <param name="gaps">The gaps the artist's or author's sources produced.</param>
    /// <param name="dismissed">Whether a gap id has been dismissed on the report.</param>
    /// <param name="wanted">The identity keys of the titles on the user's want-to-watch list, or null for none.</param>
    /// <returns>The cards.</returns>
    public static IReadOnlyList<MissingWork> Build(IEnumerable<GapItem> gaps, Func<string, bool> dismissed, IReadOnlySet<string>? wanted = null)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(dismissed);

        return gaps
            .Where(g => (g.TargetKind is BaseItemKind.MusicAlbum or BaseItemKind.Book) && !dismissed(g.Id))
            .Select(g => ToWork(g, wanted))
            .OrderByDescending(w => w.ReleaseDate ?? DateTime.MinValue)
            .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
