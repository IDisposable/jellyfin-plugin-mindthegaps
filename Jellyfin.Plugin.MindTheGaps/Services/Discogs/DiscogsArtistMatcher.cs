using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Services;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// Picks the right Discogs artist from a name search. Conservative on purpose: a Discogs name search ranks
/// by relevance but offers no work-count to rank a namesake by, so this takes the highest-ranked result
/// whose name matches the searched name exactly (after normalization), and nothing when none does. Erring
/// toward not scanning an artist is safer than scanning the wrong one (which would emit a stranger's albums
/// as gaps). Pure so a fixture exercises it.
/// </summary>
public static class DiscogsArtistMatcher
{
    /// <summary>
    /// Picks the best Discogs artist id for a searched name, or null when no result's name matches exactly.
    /// </summary>
    /// <param name="results">The artist-search results.</param>
    /// <param name="searchedName">The artist name that was searched for.</param>
    /// <returns>The chosen Discogs artist id, or null.</returns>
    public static long? Pick(IReadOnlyList<DiscogsSearchResult>? results, string? searchedName)
    {
        if (results is null || results.Count == 0)
        {
            return null;
        }

        var wanted = TextKey.Normalize(searchedName);
        if (wanted.Length == 0)
        {
            return null;
        }

        // Discogs returns results most-relevant first, so the first exact-name match is the best candidate.
        var match = results.FirstOrDefault(r => r.Id > 0 && TextKey.Normalize(r.Title) == wanted);
        return match?.Id;
    }
}
