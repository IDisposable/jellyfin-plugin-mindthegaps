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
/// what is like each owned title and accumulated the answers. Ranks what several owned titles agree on
/// first, then by TMDB popularity, and hides what the report has dismissed or muted.
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
            Titles = titles
        };
    }

    /// <summary>
    /// Finds one of the row's gaps by id, for a detail view or a Send.
    /// </summary>
    /// <param name="gapId">The gap id the row showed.</param>
    /// <returns>The gap, or <see langword="null"/> when it is not a recommendation in the current report.</returns>
    public GapItem? FindGap(string gapId)
    {
        var gap = _store.FindById(gapId);
        return gap is { Pattern: GapPattern.Recommendation } ? gap : null;
    }

    /// <summary>
    /// The pure ranking: recommendation gaps only, not dismissed, primary seed not muted, ordered by how many
    /// owned titles suggest them and then by TMDB popularity, movies and series interleaved.
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
                && !g.Adhoc
                && !resolutions.ContainsKey(g.Id)
                && (g.SourceItemId is null || !muted.Contains(g.SourceItemId)))
            .OrderByDescending(g => 1 + (g.OtherSources?.Count ?? 0))
            .ThenByDescending(g => g.SortScore ?? 0)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => MissingTitleBuilder.ToTitle(g, null, MissingTitleBuilder.Because(g)))
            .Where(t => t is not null)
            .Select(t => t!)
            .Take(Math.Max(1, limit))
            .ToList();
    }
}
