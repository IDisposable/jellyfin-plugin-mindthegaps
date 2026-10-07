using System;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Which of the scanned report's gaps are one library person's Creator works, so the person page can fold in
/// what every filmography source found (Trakt and IMDb people lists as well as TMDB). A person seeded from the
/// library owns their gaps by their Jellyfin id; one followed from an IMDb people list owns them by their IMDb
/// id, which matches the library person when they carry the same id.
/// </summary>
internal static class PersonReportGaps
{
    /// <summary>
    /// The owner id an IMDb people list gives a person.
    /// </summary>
    /// <param name="imdbId">The library person's IMDb id, or null.</param>
    /// <returns>The owner id, or <see langword="null"/> when the person carries no IMDb id.</returns>
    public static string? ImdbOwner(string? imdbId)
        => string.IsNullOrWhiteSpace(imdbId) ? null : GapSourceKeys.ImdbPerson.Owner(imdbId.Trim());

    /// <summary>
    /// Determines whether a report gap is one of this person's Creator works. An Explore pass is left out: it
    /// is a one-off look an administrator asked for, not what the scan found.
    /// </summary>
    /// <param name="gap">The report gap.</param>
    /// <param name="personKey">The library person's id, in the "N" format the filmography sources key on.</param>
    /// <param name="imdbOwner">The person's <see cref="ImdbOwner"/>, or null.</param>
    /// <returns><see langword="true"/> for one of the person's gaps.</returns>
    public static bool IsForPerson(GapItem gap, string personKey, string? imdbOwner)
    {
        ArgumentNullException.ThrowIfNull(gap);

        return gap.Pattern == GapPattern.CreatorWorks
            && !gap.Adhoc
            && string.Equals(gap.SourceItemType, SourceItemTypes.Person, StringComparison.Ordinal)
            && gap.SourceItemId is { } owner
            && (string.Equals(owner, personKey, StringComparison.OrdinalIgnoreCase)
                || (imdbOwner is not null && string.Equals(owner, imdbOwner, StringComparison.OrdinalIgnoreCase)));
    }
}
