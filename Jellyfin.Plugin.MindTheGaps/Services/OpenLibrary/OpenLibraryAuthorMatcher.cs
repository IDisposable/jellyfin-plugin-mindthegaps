using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// Picks the right OpenLibrary author from a name search, working around the namesake problem: searching a
/// common author name returns several people and the first is often the wrong one (searching "Frank Herbert"
/// returns Frank Herbert Hayward first; the Dune author is further down). Pure so the captured author-search
/// fixture exercises it.
/// </summary>
public static class OpenLibraryAuthorMatcher
{
    /// <summary>
    /// Picks the best author key for a searched name. Prefers a candidate whose name contains the searched
    /// name, then the shortest such name (a namesake usually carries extra middle names or a suffix, so
    /// "Frank Herbert" beats "Frank Herbert Hayward"), then the most works (the prolific real author outranks
    /// a thin namesake), then the key for a stable order. Returns null when nothing matches.
    /// </summary>
    /// <param name="docs">The author-search documents.</param>
    /// <param name="searchedName">The author name that was searched for.</param>
    /// <returns>The chosen author key (for example "OL79034A"), or null.</returns>
    public static string? Pick(IReadOnlyList<OpenLibraryAuthorDoc>? docs, string? searchedName)
    {
        if (docs is null || docs.Count == 0)
        {
            return null;
        }

        var wanted = Normalize(searchedName);
        var match = docs
            .Where(d => !string.IsNullOrEmpty(d.Key))
            .Where(d => wanted.Length == 0 || Normalize(d.Name).Contains(wanted, StringComparison.Ordinal))
            .OrderBy(d => Normalize(d.Name).Length)
            .ThenByDescending(d => d.WorkCount)
            .ThenBy(d => d.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        return match?.Key;
    }

    // Lowercase letters and digits only, so punctuation and spacing differences ("J. R. R. Tolkien" vs
    // "J.R.R. Tolkien", "Frank, Herbert" vs "Frank Herbert") do not block a name match.
    private static string Normalize(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }
}
