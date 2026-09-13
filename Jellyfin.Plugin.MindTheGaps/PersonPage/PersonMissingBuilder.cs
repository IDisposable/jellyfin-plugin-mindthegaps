using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.PersonPage;

/// <summary>
/// The pure half of the person page: turns a person's filmography gaps into the two lists the page shows.
/// Drops dismissed gaps (resolved, not interested, or snoozed, the same states the report hides) and
/// self-appearances (talk shows, award shows, documentaries where the person plays themself, credited as
/// "Self" or under their own name, which are not the person's work), then splits by kind and orders newest
/// first with undated titles last.
/// </summary>
internal static class PersonMissingBuilder
{
    // TMDB records a self-appearance as a character of "Self", "Himself", "Herself", or "Self - Host" and
    // similar; the mapper renders a character as "as {character}".
    private static readonly string[] _selfRoles = ["as self", "as himself", "as herself", "as themselves", "as themself"];

    /// <summary>
    /// Splits the gaps into the page's movie and series lists.
    /// </summary>
    /// <param name="gaps">The person's filmography gaps.</param>
    /// <param name="personName">The person's name, so a credit under their own name counts as a self-appearance.</param>
    /// <param name="resolutions">The current dismissals, keyed by gap id.</param>
    /// <returns>The movies and the series, each newest first.</returns>
    public static (IReadOnlyList<PersonMissingItem> Movies, IReadOnlyList<PersonMissingItem> Series) Split(
        IEnumerable<GapItem> gaps,
        string? personName,
        IReadOnlyDictionary<string, GapResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(resolutions);

        var movies = new List<PersonMissingItem>();
        var series = new List<PersonMissingItem>();
        foreach (var gap in gaps)
        {
            if (resolutions.ContainsKey(gap.Id) || IsSelfAppearance(gap.Overview, personName))
            {
                continue;
            }

            var item = ToItem(gap);
            if (item is null)
            {
                continue;
            }

            if (gap.TargetKind == BaseItemKind.Movie)
            {
                movies.Add(item);
            }
            else if (gap.TargetKind == BaseItemKind.Series)
            {
                series.Add(item);
            }
        }

        return (Order(movies), Order(series));
    }

    /// <summary>
    /// Determines whether a credit is the person appearing as themself rather than a role.
    /// </summary>
    /// <param name="role">The gap's overview, which the filmography mapper sets to "as {character}" or the crew job.</param>
    /// <param name="personName">The person's name; a character of exactly that name is the person as themself.</param>
    /// <returns><see langword="true"/> for a self-appearance.</returns>
    public static bool IsSelfAppearance(string? role, string? personName)
    {
        if (string.IsNullOrEmpty(role))
        {
            return false;
        }

        var trimmed = role.Trim();
        if (!string.IsNullOrWhiteSpace(personName)
            && trimmed.Length > 3
            && trimmed.StartsWith("as ", StringComparison.OrdinalIgnoreCase)
            && trimmed.AsSpan(3).Trim().Equals(personName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var self in _selfRoles)
        {
            if (trimmed.Equals(self, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // "as Self - Host", "as Himself (archive footage)", "as Self / Narrator".
            if (trimmed.Length > self.Length
                && trimmed.StartsWith(self, StringComparison.OrdinalIgnoreCase)
                && !char.IsLetterOrDigit(trimmed[self.Length]))
            {
                return true;
            }
        }

        return false;
    }

    private static PersonMissingItem? ToItem(GapItem gap)
    {
        string? tmdbRaw = null;
        foreach (var pair in gap.ProviderIds)
        {
            if (string.Equals(pair.Key, ProviderIds.Tmdb, StringComparison.OrdinalIgnoreCase))
            {
                tmdbRaw = pair.Value;
                break;
            }
        }

        if (!int.TryParse(tmdbRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId) || tmdbId <= 0)
        {
            return null;
        }

        return new PersonMissingItem
        {
            GapId = gap.Id,
            Title = gap.Name,
            Year = gap.Year,
            ReleaseDate = gap.ReleaseDate,
            Role = gap.Overview,
            TmdbId = tmdbId,
            ImageUrl = gap.ImageUrl,
            Upcoming = gap.IsUpcoming
        };
    }

    private static IReadOnlyList<PersonMissingItem> Order(List<PersonMissingItem> items)
        => items
            .OrderByDescending(i => i.ReleaseDate ?? DateTime.MinValue)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
