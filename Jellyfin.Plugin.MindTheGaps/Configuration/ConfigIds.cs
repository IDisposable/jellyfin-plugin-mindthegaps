using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.MindTheGaps.Configuration;

/// <summary>
/// Parses a comma-separated list of numeric ids from a configuration field (studio, keyword, list, or label
/// ids): blanks and non-numbers are dropped, only positive ids are kept, de-duplicated in input order. One
/// place so every id-list field parses the same way.
/// </summary>
internal static class ConfigIds
{
    /// <summary>
    /// Parses a comma-separated list of positive <see cref="int"/> ids.
    /// </summary>
    /// <param name="raw">The raw comma-separated value, or null.</param>
    /// <returns>The parsed ids, de-duplicated in input order.</returns>
    public static IReadOnlyList<int> ParseInts(string? raw)
        => Parse(raw, part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : (int?)null);

    /// <summary>
    /// Parses a comma-separated list of positive <see cref="long"/> ids.
    /// </summary>
    /// <param name="raw">The raw comma-separated value, or null.</param>
    /// <returns>The parsed ids, de-duplicated in input order.</returns>
    public static IReadOnlyList<long> ParseLongs(string? raw)
        => Parse(raw, part => long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : (long?)null);

    /// <summary>
    /// Parses one already-split numeric id token as an <see cref="int"/>. This is the widening half of a
    /// chip-picker descriptor (<see cref="Gaps.ExploreDescriptor"/>'s <c>Run</c>/<c>Resolve</c> and
    /// <see cref="Model.CuratedSetRef.Id"/> are strings, since a picker id is not always numeric, but most
    /// kinds are and have to parse their own back to a number before calling the client method that wants
    /// one), not the comma-split parsing <see cref="ParseInts"/> does for a whole config field.
    /// </summary>
    /// <param name="id">The token to parse.</param>
    /// <returns>The parsed id.</returns>
    public static int ParseInt(string id) => int.Parse(id, NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses one already-split numeric id token as a <see cref="long"/> (Discogs ids do not always fit an
    /// <see cref="int"/>). See <see cref="ParseInt"/> for why this exists alongside <see cref="ParseLongs"/>.
    /// </summary>
    /// <param name="id">The token to parse.</param>
    /// <returns>The parsed id.</returns>
    public static long ParseLong(string id) => long.Parse(id, NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a comma-separated list of string tokens (for example a Trakt list's numeric id or its slug):
    /// trimmed, blanks dropped, de-duplicated in input order.
    /// </summary>
    /// <param name="raw">The raw comma-separated value, or null.</param>
    /// <returns>The parsed tokens, de-duplicated in input order.</returns>
    public static IReadOnlyList<string> ParseTokens(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (seen.Add(part))
            {
                result.Add(part);
            }
        }

        return result;
    }

    private static IReadOnlyList<T> Parse<T>(string? raw, Func<string, T?> parse)
        where T : struct
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var result = new List<T>();
        var seen = new HashSet<T>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (parse(part) is { } id && seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }
}
