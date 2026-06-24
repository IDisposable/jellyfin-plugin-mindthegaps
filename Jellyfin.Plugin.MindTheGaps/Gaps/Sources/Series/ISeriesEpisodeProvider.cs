using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Services;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Supplies the canonical episode list for one owned series from a single external metadata provider's API.
/// The series-content orchestrator gathers every reachable provider for a series, orders them by the library's
/// metadata fetcher preference, and merges their lists by season: each season is owned by the highest-ranked
/// provider that lists it, so a lower provider can add a season the primary does not have but cannot contradict
/// the primary within a season it covers. The orchestrator appends the library's own virtual (missing) episodes
/// as the lowest-ranked, last-chance list, so a fresher provider's opinion always wins.
/// </summary>
public interface ISeriesEpisodeProvider
{
    /// <summary>
    /// Gets the provider this source speaks for, used to rank it against the library's metadata fetcher order.
    /// Null when the source is not a Jellyfin metadata fetcher (TVmaze), which ranks below the named fetchers.
    /// </summary>
    KnownProvider? Provider { get; }

    /// <summary>
    /// Gets the service name this source calls, so the orchestrator can skip it once that service's circuit
    /// has opened.
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// Determines, without any network call, whether this source can supply a list for the series: its
    /// cross-check is enabled, it has whatever credential it needs, and the series carries the id it resolves by.
    /// </summary>
    /// <param name="series">The owned series.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <returns><see langword="true"/> when this source can be asked for the series' episode list.</returns>
    bool CanResolve(BaseItem series, PluginConfiguration config);

    /// <summary>
    /// Fetches the canonical episode list for the series from this source.
    /// </summary>
    /// <param name="series">The owned series.</param>
    /// <param name="context">The scan context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The canonical episodes, or <see langword="null"/> when the series could not be resolved.</returns>
    Task<IReadOnlyList<CanonicalEpisode>?> GetCanonicalEpisodesAsync(
        BaseItem series,
        GapScanContext context,
        CancellationToken cancellationToken);
}
