using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Parses TheMovieDb list ids from the curated-list configuration field, accepting either a bare numeric id
/// or a pasted themoviedb.org/list/{id} URL (with or without a scheme, a www host, a name slug, or a query),
/// so a list page address can be pasted instead of hunting for its id. Comma-separated, positive ids only,
/// de-duplicated in input order.
/// </summary>
internal static class TmdbListInput
{
    /// <summary>
    /// Parses a comma-separated field of TheMovieDb list ids or list URLs.
    /// </summary>
    /// <param name="raw">The raw field value, or null.</param>
    /// <returns>The parsed list ids, de-duplicated in input order.</returns>
    public static IReadOnlyList<int> ParseIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var result = new List<int>();
        var seen = new HashSet<int>();
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
    /// Extracts the TheMovieDb list id from a single token: a bare numeric id, or a themoviedb.org/list/{id}
    /// URL. Null when the token holds no positive list id.
    /// </summary>
    /// <param name="token">A bare id or a list URL.</param>
    /// <returns>The list id, or null.</returns>
    public static int? ParseId(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var trimmed = token.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bare) && bare > 0)
        {
            return bare;
        }

        // A list URL is themoviedb.org/list/{id}{-optional-slug}{?query}; take the digits right after /list/.
        const string marker = "/list/";
        var markerIndex = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var digits = new string(trimmed[(markerIndex + marker.Length)..].TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
    }
}
