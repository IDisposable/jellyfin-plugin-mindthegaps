using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Orchestrates a gap scan: builds a library snapshot, runs every enabled source,
/// de-duplicates, and persists the resulting todo list.
/// </summary>
public sealed class GapEngine
{
    private readonly ILibraryManager _libraryManager;
    private readonly IEnumerable<IGapSource> _sources;
    private readonly GapStore _store;
    private readonly AvailabilityService _availabilityService;
    private readonly ExternalLinkEnricher _externalLinks;
    private readonly ILogger<GapEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapEngine"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="sources">The registered gap sources.</param>
    /// <param name="store">The gap store.</param>
    /// <param name="availabilityService">The availability service (optional scan-time enrichment).</param>
    /// <param name="externalLinks">Folds the host's external-url providers into each gap's links.</param>
    /// <param name="logger">The logger.</param>
    public GapEngine(
        ILibraryManager libraryManager,
        IEnumerable<IGapSource> sources,
        GapStore store,
        AvailabilityService availabilityService,
        ExternalLinkEnricher externalLinks,
        ILogger<GapEngine> logger)
    {
        _libraryManager = libraryManager;
        _sources = sources;
        _store = store;
        _availabilityService = availabilityService;
        _externalLinks = externalLinks;
        _logger = logger;
    }

    /// <summary>
    /// Runs all enabled sources and saves the resulting report.
    /// </summary>
    /// <param name="progress">Optional progress reporter (0-100).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generated report.</returns>
    public async Task<GapReport> RunAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var enabled = _sources.Where(s => s.IsEnabled(config)).ToList();
        var context = BuildContext(enabled, config);

        var gaps = new List<GapItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var total = enabled.Count;
        var completed = 0;

        foreach (var source in enabled)
        {
            // Fold this source's own 0..1 progress into its slice of the overall bar so a slow source
            // (per-item API calls) does not sit at one value for minutes.
            var sliceBase = completed / (double)total;
            var slice = 1.0 / total;
            context.SetProgressSink(f => progress?.Report((sliceBase + (Math.Clamp(f, 0.0, 1.0) * slice)) * 100.0));

            try
            {
                await foreach (var gap in source.FindGapsAsync(context, cancellationToken).ConfigureAwait(false))
                {
                    if (seen.Add(gap.Id))
                    {
                        gaps.Add(gap);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gap source {Source} failed", source.Name);
            }
            finally
            {
                context.SetProgressSink(null);
            }

            completed++;
            progress?.Report(completed * 100.0 / total);
        }

        // Let the host's external-url providers contribute links (TMDB/IMDb from core, JustWatch from
        // that plugin if installed), keeping the hand-built links as a fallback for what core misses.
        _externalLinks.Enrich(gaps);

        if (config.FetchAvailabilityDuringScan)
        {
            await EnrichAvailabilityAsync(gaps, config, cancellationToken).ConfigureAwait(false);
        }

        var report = new GapReport
        {
            GeneratedUtc = DateTime.UtcNow,
            TotalGaps = gaps.Count,
            Items = gaps
        };

        _store.Save(report);
        return report;
    }

    // Opt-in: look up "where to watch" for each watchable gap during the scan so the report can filter
    // to streamable gaps. Bounded by MaxAvailabilityLookups; lookups are cached, so re-scans are cheap.
    private async Task EnrichAvailabilityAsync(IReadOnlyList<GapItem> gaps, PluginConfiguration config, CancellationToken cancellationToken)
    {
        var looked = 0;
        var enriched = 0;
        foreach (var gap in gaps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (looked >= GapScanLimits.MaxAvailabilityLookups)
            {
                _logger.LogInformation("Availability enrichment hit the cap ({Cap}); remaining gaps stay lazy", GapScanLimits.MaxAvailabilityLookups);
                break;
            }

            var watchable = gap.TargetKind is Jellyfin.Data.Enums.BaseItemKind.Movie or Jellyfin.Data.Enums.BaseItemKind.Series;
            if (!watchable || !gap.ProviderIds.ContainsKey(GapScanContext.TmdbProvider))
            {
                continue;
            }

            looked++;
            try
            {
                var offers = await _availabilityService.GetOffersAsync(
                    new AvailabilityQuery
                    {
                        TargetKind = gap.TargetKind,
                        ProviderIds = gap.ProviderIds,
                        Title = gap.Name,
                        Year = gap.Year,
                        Country = config.MetadataCountryCode
                    },
                    config,
                    cancellationToken).ConfigureAwait(false);

                if (offers.Count > 0)
                {
                    gap.Availability = offers;
                    enriched++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Availability enrichment failed for '{Name}'", gap.Name);
            }
        }

        _logger.LogInformation("Availability enrichment: looked up {Looked}, {Enriched} have offers", looked, enriched);
    }

    private GapScanContext BuildContext(IReadOnlyCollection<IGapSource> enabledSources, PluginConfiguration config)
    {
        // The kinds to index are declared by the sources themselves; the engine just unions them.
        var kinds = enabledSources.SelectMany(s => s.OwnedKinds).Distinct().ToArray();
        var byKey = new Dictionary<string, BaseItem>();
        var itemCount = 0;

        if (kinds.Length > 0)
        {
            var owned = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = kinds,
                Recursive = true
            });
            itemCount = owned.Count;
            foreach (var item in owned)
            {
                var kind = item.GetBaseItemKind();
                foreach (var providerId in item.ProviderIds)
                {
                    if (!string.IsNullOrEmpty(providerId.Value))
                    {
                        byKey[OwnershipIndex.MakeKey(kind, providerId.Key, providerId.Value)] = item;
                    }
                }
            }
        }

        var ownership = new OwnershipIndex(byKey);
        _logger.LogInformation(
            "Ownership index: {Items} owned items, {Keys} provider-id keys (an item has several ids), across kinds [{Kinds}]. Series content (missing episodes) is scanned separately and is not in this index",
            itemCount,
            ownership.Count,
            string.Join(", ", kinds));

        return new GapScanContext(config, ownership);
    }
}
