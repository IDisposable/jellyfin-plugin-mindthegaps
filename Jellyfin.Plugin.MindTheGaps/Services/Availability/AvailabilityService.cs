using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// Aggregates streaming-availability across all enabled sources and de-duplicates the result.
/// </summary>
public sealed class AvailabilityService
{
    private readonly IEnumerable<IAvailabilitySource> _sources;
    private readonly ILogger<AvailabilityService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvailabilityService"/> class.
    /// </summary>
    /// <param name="sources">The availability sources.</param>
    /// <param name="logger">The logger.</param>
    public AvailabilityService(IEnumerable<IAvailabilitySource> sources, ILogger<AvailabilityService> logger)
    {
        _sources = sources;
        _logger = logger;
    }

    /// <summary>
    /// Gets de-duplicated offers for a title from all enabled sources.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The merged offers.</returns>
    public async Task<IReadOnlyList<AvailabilityOffer>> GetOffersAsync(
        AvailabilityQuery query,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var merged = new List<AvailabilityOffer>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in _sources)
        {
            if (!source.IsEnabled(config))
            {
                continue;
            }

            try
            {
                foreach (var offer in await source.GetOffersAsync(query, cancellationToken).ConfigureAwait(false))
                {
                    if (seen.Add(offer.Provider + "|" + (offer.MonetizationType ?? string.Empty)))
                    {
                        merged.Add(offer);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Availability source {Source} failed", source.Name);
            }
        }

        return merged;
    }
}
