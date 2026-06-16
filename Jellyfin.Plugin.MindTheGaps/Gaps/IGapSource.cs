using System.Collections.Generic;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// A pluggable producer of gaps (collections, filmographies, recommendations, ...).
/// </summary>
public interface IGapSource
{
    /// <summary>
    /// Gets the display name of the source.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the library item kinds this source diffs against. The engine unions these across all
    /// enabled sources and indexes exactly those kinds into the <see cref="OwnershipIndex"/>,
    /// so the engine never hard-codes which kinds to load.
    /// </summary>
    IReadOnlyCollection<BaseItemKind> OwnedKinds { get; }

    /// <summary>
    /// Determines whether this source is enabled for the given configuration.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <returns><see langword="true"/> if enabled.</returns>
    bool IsEnabled(PluginConfiguration config);

    /// <summary>
    /// Streams the gaps discovered by this source.
    /// </summary>
    /// <param name="context">The scan context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async stream of gaps.</returns>
    IAsyncEnumerable<GapItem> FindGapsAsync(GapScanContext context, CancellationToken cancellationToken);
}
