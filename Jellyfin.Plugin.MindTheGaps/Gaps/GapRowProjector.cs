using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Reduces full gaps to the rows the report list loads (see <see cref="GapRow"/>).
/// </summary>
internal static class GapRowProjector
{
    /// <summary>
    /// Projects gaps to a report of rows. What repeats across rows travels once: each distinct source-link
    /// list and target kind is indexed, and a pattern or domain every row shares is stated on the report
    /// instead of the rows (a tab request is already narrowed to one of each).
    /// </summary>
    /// <param name="items">The gaps.</param>
    /// <returns>A report holding the rows and the tables they index. The caller fills in the report's own metadata.</returns>
    public static GapRowReport Project(IReadOnlyList<GapItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var sharedPattern = items.Count > 0 && items.All(i => i.Pattern == items[0].Pattern);
        var sharedDomain = items.Count > 0 && items.All(i => i.Domain == items[0].Domain);
        var tables = new Tables();
        var rows = new GapRow[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            rows[i] = ToRow(items[i], tables, sharedPattern, sharedDomain);
        }

        return new GapRowReport
        {
            PatternName = sharedPattern ? items[0].PatternName : null,
            DomainName = sharedDomain ? items[0].DomainName : null,
            TargetKinds = tables.Kinds,
            Items = rows,
            SourceLinkSets = tables.LinkSets
        };
    }

    private static GapRow ToRow(GapItem item, Tables tables, bool sharedPattern, bool sharedDomain) => new()
    {
        Id = item.Id,
        PatternName = sharedPattern ? null : item.PatternName,
        DomainName = sharedDomain ? null : item.DomainName,
        TargetKindRef = tables.KindIndex(item.TargetKind),
        Name = item.Name,
        Year = item.Year,
        Season = item.Season,
        ReleaseDate = item.ReleaseDate,
        IsUpcoming = item.IsUpcoming,
        ImageUrl = item.ImageUrl,
        HasOverview = !string.IsNullOrEmpty(item.Overview),
        ProviderIds = item.ProviderIds.Count == 0 ? null : item.ProviderIds,
        SourceItemId = item.SourceItemId,
        SourceItemName = item.SourceItemName,
        SourceItemType = item.SourceItemType,
        SourceItemYear = item.SourceItemYear,
        SourceLinksRef = tables.LinkSetIndex(item.SourceLinks),
        OtherSources = item.OtherSources,
        SetOwnedCount = item.SetOwnedCount,
        SetTotalCount = item.SetTotalCount,
        SortScore = item.SortScore,
        LibraryItemId = item.LibraryItemId,
        SeasonItemId = item.SeasonItemId,
        WatchTmdbId = item.WatchTmdbId,
        Availability = Badges(item.Availability),
        WatchUrl = WatchUrl(item.Availability),
        AvailabilityChecked = item.AvailabilityChecked
    };

    // The offers of one title all carry TMDB's one watch page for it, so the first usable one stands for all.
    private static string? WatchUrl(IReadOnlyList<AvailabilityOffer> offers)
        => offers.Select(o => o.Url).FirstOrDefault(TmdbLinks.IsWatchUrl);

    // Distinct by service and how it is offered: a title on Netflix in three qualities is one badge.
    private static AvailabilityBadge[]? Badges(IReadOnlyList<AvailabilityOffer> offers)
    {
        if (offers.Count == 0)
        {
            return null;
        }

        return offers
            .Select(o => new AvailabilityBadge { Provider = o.Provider, MonetizationType = o.MonetizationType, LogoUrl = o.LogoUrl })
            .DistinctBy(b => (b.Provider, b.MonetizationType))
            .ToArray();
    }

    // The lookup tables rows index into, built as the rows are.
    private sealed class Tables
    {
        private readonly Dictionary<string, int> _linkSetIndex = new(StringComparer.Ordinal);
        private readonly Dictionary<BaseItemKind, int> _kindIndex = [];

        public List<IReadOnlyList<ExternalLink>> LinkSets { get; } = [];

        public List<string> Kinds { get; } = [];

        public int KindIndex(BaseItemKind kind)
        {
            if (!_kindIndex.TryGetValue(kind, out var index))
            {
                index = Kinds.Count;
                Kinds.Add(kind.ToString());
                _kindIndex[kind] = index;
            }

            return index;
        }

        public int? LinkSetIndex(IReadOnlyList<ExternalLink> links)
        {
            if (links.Count == 0)
            {
                return null;
            }

            var key = string.Join('\n', links.Select(l => string.Concat(l.Name, "\t", l.Url)));
            if (!_linkSetIndex.TryGetValue(key, out var index))
            {
                index = LinkSets.Count;
                LinkSets.Add(links);
                _linkSetIndex[key] = index;
            }

            return index;
        }
    }
}
