using System;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Services;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Folds an episode title for cross-numbering comparison: a trailing part marker is stripped, then the
/// shared text normalizer is applied, so the two halves of a two-part episode ("X (1)", "X Part 2") fold
/// to one key and match a library that merged them into a single file. This is episode-specific and
/// separate from <see cref="TextKey.Normalize"/>, which the music and book name-key ownership uses and
/// which must keep two numbered volumes distinct.
/// </summary>
internal static class EpisodeTitleKey
{
    // Word and roman forms of a small part number.
    private static readonly string[] PartNumberWords =
    [
        "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x"
    ];

    /// <summary>
    /// Folds a title to its comparison key: a trailing part marker removed, then normalized.
    /// </summary>
    /// <param name="title">The episode title.</param>
    /// <returns>The folded key, or an empty string when the title is blank.</returns>
    public static string Of(string? title) => TextKey.Normalize(StripPartMarker(title));

    // Strips a trailing part marker (" (2)", ", Part 2", " Part Two", " Pt. 2", " Part II") so the two parts
    // of a two-part episode fold to one title.
    private static string StripPartMarker(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var s = title.TrimEnd();

        // "... (2)": a short parenthesized number, not a four-digit year.
        if (s[^1] == ')')
        {
            var open = s.LastIndexOf('(');
            if (open > 0)
            {
                var inner = s[(open + 1)..^1].Trim();
                if (inner.Length is > 0 and <= 2 && inner.All(char.IsDigit))
                {
                    return TrimMarkerTail(s[..open]);
                }
            }
        }

        // "... Part 2" / "... , Part 2" / "... Pt. Two" / "... Part II".
        var words = s.Split(' ');
        if (words.Length >= 2 && IsPartWord(words[^2]) && IsPartNumber(words[^1]))
        {
            return TrimMarkerTail(string.Join(' ', words[..^2]));
        }

        return s;
    }

    private static string TrimMarkerTail(string s) => s.TrimEnd().TrimEnd(',', ':', '-').TrimEnd();

    private static bool IsPartWord(string word)
        => word.Equals("part", StringComparison.OrdinalIgnoreCase)
        || word.Equals("pt", StringComparison.OrdinalIgnoreCase)
        || word.Equals("pt.", StringComparison.OrdinalIgnoreCase);

    private static bool IsPartNumber(string word)
    {
        if (word.Length is > 0 and <= 2 && word.All(char.IsDigit))
        {
            return true;
        }

        foreach (var token in PartNumberWords)
        {
            if (word.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
