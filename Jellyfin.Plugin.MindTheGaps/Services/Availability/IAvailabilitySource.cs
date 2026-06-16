using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// A pluggable source of streaming-availability offers (TMDB watch/providers, JustWatch, ...).
/// </summary>
public interface IAvailabilitySource
{
    /// <summary>
    /// Gets the display name of the source.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Determines whether this source is enabled for the given configuration.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <returns><see langword="true"/> if enabled.</returns>
    bool IsEnabled(PluginConfiguration config);

    /// <summary>
    /// Gets offers for a title.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The offers (possibly empty).</returns>
    Task<IReadOnlyList<AvailabilityOffer>> GetOffersAsync(AvailabilityQuery query, CancellationToken cancellationToken);
}
