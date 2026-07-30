using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Imdb;

/// <summary>
/// Parses the IMDb list configuration field. A token is either a list id ("ls055576446") or a user id
/// ("ur1000000", meaning that user's watchlist), given bare or as a pasted imdb.com URL. IMDb's newer
/// pseudonymous profile ids (the "p." form in an imdb.com/user/p.xxxx/watchlist/ address) are rejected: the
/// API validates the "ur" form only, so accepting them would fail at fetch time instead of at entry.
/// Comma-separated, de-duplicated in input order.
/// </summary>
internal static class ImdbListInput
{
    /// <summary>
    /// Parses a comma-separated field of IMDb list ids, user ids, or imdb.com URLs.
    /// </summary>
    /// <param name="raw">The raw field value, or null.</param>
    /// <returns>The parsed ids, de-duplicated in input order.</returns>
    public static IReadOnlyList<string> ParseIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ParseId(part) is { } id && seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts the IMDb id from a single token. Null when the token holds neither an "ls" list id nor a "ur"
    /// user id.
    /// </summary>
    /// <param name="token">A bare id or an imdb.com URL.</param>
    /// <returns>The id, lower-cased prefix and all, or null.</returns>
    public static string? ParseId(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        // Either form is a prefix plus digits wherever it appears, so scan the token for one rather than
        // matching URL shapes: /list/ls123/, /user/ur123/watchlist/, and a bare id all land the same way.
        var trimmed = token.Trim();
        return Find(trimmed, "ls") ?? Find(trimmed, "ur");
    }

    private static string? Find(string token, string prefix)
    {
        var index = token.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            // Reject a match inside a longer word ("urls", "controls"): the id starts the token or follows a
            // separator, and at least one digit has to follow the prefix.
            var startsCleanly = index == 0 || !char.IsAsciiLetterOrDigit(token[index - 1]);
            var digits = new string(token[(index + prefix.Length)..].TakeWhile(char.IsAsciiDigit).ToArray());
            if (startsCleanly && digits.Length > 0)
            {
                return string.Concat(prefix.ToLowerInvariant(), digits);
            }

            index = token.IndexOf(prefix, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }
}
