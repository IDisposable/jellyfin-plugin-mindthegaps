using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// A gap source that can re-check a single owned series for missing episodes, so the dashboard can verify
/// a fix for one series without a full rescan. Implemented by the series-content sources (the library
/// reader and the TVmaze/TheTVDB cross-checks); the engine runs them all for one series and merges the
/// result into the report.
/// </summary>
public interface ISeriesContentSource
{
    /// <summary>
    /// Re-checks one owned series for the episodes it is missing.
    /// </summary>
    /// <param name="series">The owned series.</param>
    /// <param name="context">The scan context (config and ownership).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The series' missing episodes as gaps, empty if it has none or cannot be resolved.</returns>
    Task<IReadOnlyList<GapItem>> CheckSeriesAsync(BaseItem series, GapScanContext context, CancellationToken cancellationToken);
}
