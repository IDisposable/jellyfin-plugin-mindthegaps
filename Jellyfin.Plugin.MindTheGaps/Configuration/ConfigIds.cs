using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.MindTheGaps.Configuration;

/// <summary>
/// Parses a comma-separated list of numeric ids from a configuration field (studio, keyword, list, or label
/// ids): blanks and non-numbers are dropped, only positive ids are kept, de-duplicated in input order. One
/// place so every id-list field parses the same way.
/// </summary>
public static class ConfigIds
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
