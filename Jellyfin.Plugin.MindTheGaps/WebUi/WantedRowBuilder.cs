using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The pure shaping of the home screen's want-to-watch row: the owned titles from the user's want-to-watch
/// playlist when the row includes them, then the movies and series on their list that are not done and that the
/// library does not hold, each group with the ones added last first.
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
        => Build(entries, ownership, [], limit, now);

    /// <summary>
    /// Builds the row with the user's owned titles ahead of the missing ones. What is ready to watch comes
    /// first. Each group gets the limit to itself, so a long playlist can never crowd the missing titles off
    /// the row, nor a long want-list crowd out the owned ones.
    /// </summary>
    /// <param name="entries">The user's todo entries.</param>
    /// <param name="ownership">The index of the owned movies and series.</param>
    /// <param name="owned">The owned movies and series to show, already in their order (newest first).</param>
    /// <param name="limit">The most titles to return.</param>
    /// <param name="now">The current time, for what is not released yet.</param>
    /// <returns>The cards.</returns>
    public static IReadOnlyList<MissingTitle> Build(IEnumerable<TodoEntry> entries, OwnershipIndex ownership, IEnumerable<BaseItem> owned, int limit, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(owned);

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

        var perGroup = Math.Max(1, limit);
        var missing = cards
            .OrderByDescending(c => c.Added, StringComparer.Ordinal)
            .ThenBy(c => c.Card.Title, StringComparer.OrdinalIgnoreCase)
            .Take(perGroup)
            .Select(c => c.Card);

        return owned
            .Where(item => item is not null)
            .DistinctBy(item => item.Id)
            .Take(perGroup)
            .Select(OwnedCard)
            .Concat(missing)
            .ToList();
    }

    /// <summary>
    /// Shapes an owned movie or series as a card. It carries its library item, which the client opens and
    /// draws the poster of; the TMDB id is there when the item has one, for the same lookups any card gets.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The card.</returns>
    internal static MissingTitle OwnedCard(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new MissingTitle
        {
            GapId = "owned:" + item.Id.ToString("N", CultureInfo.InvariantCulture),
            Title = item.Name ?? string.Empty,
            Year = item.ProductionYear,
            ReleaseDate = item.PremiereDate,
            Kind = item is Series ? "Series" : "Movie",
            TmdbId = item.TryGetProviderIdAsInt(ProviderIds.Tmdb, out var tmdbId) ? tmdbId : 0,
            OnList = true,
            ItemId = item.Id
        };
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
