using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The pure shaping of the home screen's want-to-watch row: the movies and series on a user's list that are
/// not done and that the library does not hold, the ones added last first.
/// </summary>
internal static class WantedRowBuilder
{
    /// <summary>
    /// Builds the row.
    /// </summary>
    /// <param name="entries">The user's todo entries.</param>
    /// <param name="ownership">The index of the owned movies and series.</param>
    /// <param name="limit">The most titles to return.</param>
    /// <param name="now">The current time, for what is not released yet.</param>
    /// <returns>The cards.</returns>
    public static IReadOnlyList<MissingTitle> Build(IEnumerable<TodoEntry> entries, OwnershipIndex ownership, int limit, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(ownership);

        var cards = new List<(string Added, MissingTitle Card)>();
        foreach (var entry in entries)
        {
            if (entry.Done
                || !TryKind(entry.TargetKindName, out var kind)
                || !entry.ProviderIds.TryGetProviderIdAsInt(ProviderIds.Tmdb, out var tmdbId)
                || ownership.OwnsAny(kind, entry.ProviderIds))
            {
                continue;
            }

            cards.Add((entry.AddedUtc, new MissingTitle
            {
                GapId = entry.Id,
                Title = entry.Name,
                Year = entry.Year,
                ReleaseDate = entry.ReleaseDate,
                Kind = kind == BaseItemKind.Series ? "Series" : "Movie",
                TmdbId = tmdbId,
                ImageUrl = entry.ImageUrl,
                Upcoming = entry.ReleaseDate is { } released && released > now,
                OnList = true
            }));
        }

        return cards
            .OrderByDescending(c => c.Added, StringComparer.Ordinal)
            .ThenBy(c => c.Card.Title, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Card)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    private static bool TryKind(string name, out BaseItemKind kind)
    {
        kind = name switch
        {
            "Movie" => BaseItemKind.Movie,
            "Series" => BaseItemKind.Series,
            _ => default
        };
        return name is "Movie" or "Series";
    }
}
