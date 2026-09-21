using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The pure shaping shared by every web UI surface: a gap becomes a card, duplicates of one title merge,
/// and a list orders newest first with undated titles last.
/// </summary>
internal static class MissingTitleBuilder
{
    /// <summary>
    /// The separator between two credits merged on one card.
    /// </summary>
    public const string RoleSeparator = "\u2009·\u2009";

    /// <summary>
    /// Shapes one gap into a card.
    /// </summary>
    /// <param name="gap">The gap.</param>
    /// <param name="role">The person's credit, on a person page; otherwise null.</param>
    /// <param name="because">Why it is suggested, on a recommendation; otherwise null.</param>
    /// <returns>The card, or <see langword="null"/> when the gap carries no usable TMDB id.</returns>
    public static MissingTitle? ToTitle(GapItem gap, string? role, string? because)
    {
        ArgumentNullException.ThrowIfNull(gap);

        if (!gap.ProviderIds.TryGetProviderIdAsInt(ProviderIds.Tmdb, out var tmdbId))
        {
            return null;
        }

        return new MissingTitle
        {
            GapId = gap.Id,
            Kind = gap.TargetKind == BaseItemKind.Movie ? "Movie" : "Series",
            Title = gap.Name,
            Year = gap.Year,
            ReleaseDate = gap.ReleaseDate,
            Role = role,
            Because = because,
            TmdbId = tmdbId,
            ImageUrl = gap.ImageUrl,
            Upcoming = gap.IsUpcoming
        };
    }

    /// <summary>
    /// Joins two credits on one title into one label, skipping an empty or repeated credit.
    /// </summary>
    /// <param name="first">The credit already on the item.</param>
    /// <param name="second">The credit to add.</param>
    /// <returns>The combined label.</returns>
    public static string? MergeRoles(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(second))
        {
            return first;
        }

        if (string.IsNullOrWhiteSpace(first))
        {
            return second;
        }

        second = second.Trim();

        foreach (var part in first.Split(RoleSeparator, StringSplitOptions.TrimEntries))
        {
            if (part.Equals(second, StringComparison.OrdinalIgnoreCase))
            {
                return first;
            }
        }

        return first + RoleSeparator + second;
    }

    /// <summary>
    /// The "Because you have …" label for a recommendation: its primary seed title plus any other seeds
    /// that suggested it, so a title several owned titles point at says so. A title that came from a list
    /// reads "From" and the list's name instead, since the list is not something the library owns.
    /// </summary>
    /// <param name="gap">The recommendation gap.</param>
    /// <param name="shown">The number of seed titles to show before summarizing the rest.</param>
    /// <returns>The label, or <see langword="null"/> when the gap names no seed.</returns>
    public static string? Because(GapItem gap, int shown = 2)
    {
        ArgumentNullException.ThrowIfNull(gap);

        if (gap.SourceItemType is not (null or SourceItemTypes.Movie or SourceItemTypes.Series)
            && !string.IsNullOrWhiteSpace(gap.SourceItemName))
        {
            return "From " + gap.SourceItemName.Trim();
        }

        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(gap.SourceItemName))
        {
            names.Add(gap.SourceItemName.Trim());
        }

        foreach (var other in gap.OtherSources ?? [])
        {
            var otherName = other.Name?.Trim();

            if (string.IsNullOrWhiteSpace(otherName))
            {
                continue;
            }

            if (!names.Contains(otherName, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(otherName);
            }
        }

        if (names.Count == 0)
        {
            return null;
        }

        var label = string.Join(", ", names.Take(shown));
        if (names.Count > shown)
        {
            label += string.Create(CultureInfo.InvariantCulture, $" and {names.Count - shown} more");
        }

        return "Because you have " + label;
    }

    /// <summary>
    /// Orders cards newest first, undated last, ties by title.
    /// </summary>
    /// <param name="titles">The cards.</param>
    /// <returns>The ordered list.</returns>
    public static IEnumerable<MissingTitle> NewestFirst(IEnumerable<MissingTitle> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);

        return titles
            .OrderByDescending(i => i.ReleaseDate ?? DateTime.MinValue)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase);
    }
}
