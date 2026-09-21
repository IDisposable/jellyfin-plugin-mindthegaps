using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The home screen's discovery row. Unlike the per-page surfaces, this one reads the scanned report rather
/// than asking TMDB on demand: a home row is about the whole library, and the scan has already asked TMDB
/// what is like each owned title and accumulated the answers. Ranks what several sources agree on first, then
/// by TMDB popularity, shows only what owned titles suggest and what public lists carry, and hides what the
/// report has dismissed or muted.
/// </summary>
public sealed class HomeDiscoverService
{
    private readonly GapStore _store;
    private readonly ResolutionStore _resolutions;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeDiscoverService"/> class.
    /// </summary>
    /// <param name="store">The gap store.</param>
    /// <param name="resolutions">The dismissals.</param>
    public HomeDiscoverService(GapStore store, ResolutionStore resolutions)
    {
        _store = store;
        _resolutions = resolutions;
    }

    /// <summary>
    /// Builds the row.
    /// </summary>
    /// <param name="isAdministrator">Whether the caller is an administrator, which gates the Send buttons.</param>
    /// <param name="limit">The most titles to return.</param>
    /// <returns>The row; empty when the scan has not produced recommendations yet.</returns>
    public HomeDiscoverResult Get(bool isAdministrator, int limit)
    {
        var config = Plugin.RequireConfiguration();
        var titles = Rank(_store.LoadSnapshot().Items, _resolutions.GetAll(), limit);
        return new HomeDiscoverResult
        {
            CanSendMovies = isAdministrator && AcquisitionService.RadarrConfigured(config),
            CanSendSeries = isAdministrator && AcquisitionService.SonarrConfigured(config),
            CanTodo = isAdministrator,
            Titles = titles
        };
    }

    /// <summary>
    /// Finds one of the row's gaps by id, for a Send.
    /// </summary>
    /// <param name="gapId">The gap id the row showed.</param>
    /// <returns>The gap, or <see langword="null"/> when it is not a recommendation in the current report.</returns>
    public GapItem? FindGap(string gapId)
    {
        var gap = _store.FindById(gapId);
        return gap is { Pattern: GapPattern.Recommendation } && IsShownOnRow(gap) ? gap : null;
    }

    /// <summary>
    /// Whether a recommendation may be shown on the row. The row is any signed-in user's, so it is limited to
    /// what owned titles suggest and to lists that are public by construction
    /// (<see cref="SourceItemTypes.PublicListKinds"/>). A gap from a watchlist, a wantlist, favorites, or a
    /// list the plugin cannot tell is public belongs to the account that follows it. A title an owned title
    /// also suggests but that a private list claimed first is left out with it, since the gap's primary source
    /// is the list.
    /// </summary>
    /// <param name="gap">The recommendation gap.</param>
    /// <returns><see langword="true"/> when the gap's primary source is an owned movie or series, or a public list.</returns>
    public static bool IsShownOnRow(GapItem gap)
    {
        ArgumentNullException.ThrowIfNull(gap);

        return FromOwnedTitle(gap)
            || (gap.SourceItemType is { } type && SourceItemTypes.PublicListKinds.Contains(type, StringComparer.Ordinal));
    }

    /// <summary>
    /// The pure ranking: recommendations the row may show (<see cref="IsShownOnRow"/>), not dismissed, primary
    /// seed not muted, ordered by how many sources agree, then owned-title recommendations before list titles,
    /// then by TMDB popularity, movies and series interleaved.
    /// </summary>
    /// <param name="items">The report's gaps.</param>
    /// <param name="resolutions">The current dismissals, keyed by gap id or "recsource:{guid}".</param>
    /// <param name="limit">The most titles to return.</param>
    /// <returns>The ranked cards.</returns>
    public static IReadOnlyList<MissingTitle> Rank(IEnumerable<GapItem> items, IReadOnlyDictionary<string, GapResolution> resolutions, int limit)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(resolutions);

        var muted = new HashSet<string>(
            resolutions.Keys
                .Where(k => k.StartsWith(GapResolution.RecSourcePrefix, StringComparison.Ordinal))
                .Select(k => k[GapResolution.RecSourcePrefix.Length..]),
            StringComparer.OrdinalIgnoreCase);

        return items
            .Where(g => g.Pattern == GapPattern.Recommendation
                && (g.TargetKind == BaseItemKind.Movie || g.TargetKind == BaseItemKind.Series)
                && IsShownOnRow(g)
                && !g.Adhoc
                && !resolutions.ContainsKey(g.Id)
                && (g.SourceItemId is null || !muted.Contains(g.SourceItemId)))
            .OrderByDescending(g => 1 + (g.OtherSources?.Count ?? 0))
            .ThenBy(g => FromOwnedTitle(g) ? 0 : 1)
            .ThenByDescending(g => g.SortScore ?? 0)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => MissingTitleBuilder.ToTitle(g, null, MissingTitleBuilder.Because(g)))
            .Where(t => t is not null)
            .Select(t => t!)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    // Owned movies and series only. The row's cards are TMDB titles, so an owned artist or book as the source of
    // a recommendation is left out here, and Rank takes movie and series targets alone; a row that shows albums
    // and books changes both.
    private static bool FromOwnedTitle(GapItem gap)
        => gap.SourceItemType is SourceItemTypes.Movie or SourceItemTypes.Series;
}
