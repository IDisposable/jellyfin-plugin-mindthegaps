using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;

/// <summary>
/// Picks the right OpenLibrary author from a name search, working around the namesake problem: searching a
/// common author name returns several people and the first is often the wrong one (searching "Frank Herbert"
/// returns Frank Herbert Hayward first; the Dune author is further down). Pure so the captured author-search
/// fixture exercises it.
/// </summary>
internal static class OpenLibraryAuthorMatcher
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

        // TextKey.Normalize folds out punctuation and spacing ("J. R. R. Tolkien" vs "J.R.R. Tolkien",
        // "Frank, Herbert" vs "Frank Herbert") so they do not block a name match.
        var wanted = TextKey.Normalize(searchedName);
        var match = docs
            .Where(d => !string.IsNullOrEmpty(d.Key))
            .Where(d => wanted.Length == 0 || TextKey.Normalize(d.Name).Contains(wanted, StringComparison.Ordinal))
            .OrderBy(d => TextKey.Normalize(d.Name).Length)
            .ThenByDescending(d => d.WorkCount)
            .ThenBy(d => d.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        return match?.Key;
    }
}
