using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The pure half of the person page: turns a person's filmography gaps into the two lists the page shows.
/// Drops dismissed gaps (resolved, not interested, or snoozed, the same states the report hides) and
/// self-appearances (talk shows, award shows, documentaries where the person plays themself, credited as
/// "Self" or under their own name, which are not the person's work), merges a title the person is credited
/// on more than once (cast and crew, or two jobs) into one entry listing every credit, then splits by kind
/// and orders newest first with undated titles last.
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
    public static (IReadOnlyList<MissingTitle> Movies, IReadOnlyList<MissingTitle> Series) Split(
        IEnumerable<GapItem> gaps,
        string? personName,
        IReadOnlyDictionary<string, GapResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(resolutions);

        // The mapper emits one gap per credit, so a title the person acted in and directed arrives twice
        // under the same gap id; the page shows it once with both credits.
        var movies = new Dictionary<string, MissingTitle>(StringComparer.Ordinal);
        var series = new Dictionary<string, MissingTitle>(StringComparer.Ordinal);
        foreach (var gap in gaps)
        {
            if (resolutions.ContainsKey(gap.Id) || IsSelfAppearance(gap.Overview, personName))
            {
                continue;
            }

            var into = gap.TargetKind == BaseItemKind.Movie ? movies : gap.TargetKind == BaseItemKind.Series ? series : null;
            if (into is null)
            {
                continue;
            }

            if (into.TryGetValue(gap.Id, out var existing))
            {
                existing.Role = MissingTitleBuilder.MergeRoles(existing.Role, gap.Overview);
                continue;
            }

            var item = MissingTitleBuilder.ToTitle(gap, gap.Overview, null);
            if (item is not null)
            {
                into.Add(gap.Id, item);
            }
        }

        return (MissingTitleBuilder.NewestFirst(movies.Values).ToList(), MissingTitleBuilder.NewestFirst(series.Values).ToList());
    }

    /// <summary>
    /// Joins every credit the person has on one title into one label.
    /// </summary>
    /// <param name="credits">The gaps for one title, one per credit.</param>
    /// <returns>The merged label.</returns>
    public static string? MergedRole(IEnumerable<GapItem> credits)
    {
        ArgumentNullException.ThrowIfNull(credits);

        string? role = null;
        foreach (var credit in credits)
        {
            role = MissingTitleBuilder.MergeRoles(role, credit.Overview);
        }

        return role;
    }

    /// <summary>
    /// Determines whether a credit is the person appearing as themself rather than a role.
    /// </summary>
    /// <param name="role">The gap's overview, which the filmography mapper sets to "as {character}" or the crew job.</param>
    /// <param name="personName">The person's name; a character of that name (allowing a suffix, "David Rappo -
    /// Cameo") is treated the same as the fixed self-roles below.</param>
    /// <returns><see langword="true"/> for a self-appearance.</returns>
    public static bool IsSelfAppearance(string? role, string? personName)
    {
        if (string.IsNullOrEmpty(role))
        {
            return false;
        }

        var trimmed = role.Trim();
        var candidates = string.IsNullOrWhiteSpace(personName)
            ? _selfRoles
            : _selfRoles.Append("as " + personName.Trim());

        foreach (var self in candidates)
        {
            if (trimmed.Equals(self, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // "as Self - Host", "as Himself (archive footage)", "as David Rappo - Cameo".
            if (trimmed.Length > self.Length
                && trimmed.StartsWith(self, StringComparison.OrdinalIgnoreCase)
                && !char.IsLetterOrDigit(trimmed[self.Length]))
            {
                return true;
            }
        }

        return false;
    }
}
