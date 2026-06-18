using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
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
    private readonly ExternalLinkEnricher _externalLinks;
    private readonly Services.Webhook.WebhookNotifier _webhook;
    private readonly ILogger<GapEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapEngine"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="sources">The registered gap sources.</param>
    /// <param name="store">The gap store.</param>
    /// <param name="externalLinks">Folds the host's external-url providers into each gap's links.</param>
    /// <param name="webhook">Posts a completion notification, if a webhook is configured.</param>
    /// <param name="logger">The logger.</param>
    public GapEngine(
        ILibraryManager libraryManager,
        IEnumerable<IGapSource> sources,
        GapStore store,
        ExternalLinkEnricher externalLinks,
        Services.Webhook.WebhookNotifier webhook,
        ILogger<GapEngine> logger)
    {
        _libraryManager = libraryManager;
        _sources = sources;
        _store = store;
        _externalLinks = externalLinks;
        _webhook = webhook;
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

        var priorIds = new HashSet<string>(_store.Load().Items.Select(i => i.Id), StringComparer.Ordinal);

        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);

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
                    if (byId.TryGetValue(gap.Id, out var existing))
                    {
                        MergeDuplicateSource(existing, gap);
                    }
                    else
                    {
                        byId[gap.Id] = gap;
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

        // Carry the previous report's enrichment forward by id (resolved external ids and "where to
        // watch") so a rescan does not throw away what the background pass found; it only needs to look
        // up genuinely new gaps. Do this before the host link pass so carried ids produce their links.
        CarryForward(gaps);

        // Let the host's external-url providers contribute links (TMDB/IMDb from core, JustWatch from
        // that plugin if installed), keeping the hand-built links as a fallback for what core misses.
        _externalLinks.Enrich(gaps);

        var report = new GapReport
        {
            GeneratedUtc = DateTime.UtcNow,
            GeneratedVersion = Plugin.Instance?.Version?.ToString() ?? string.Empty,
            TotalGaps = gaps.Count,
            Items = gaps
        };

        _store.Save(report);

        var newCount = gaps.Count(g => !priorIds.Contains(g.Id));
        await _webhook.NotifyAsync(
            "scan",
            string.Create(CultureInfo.InvariantCulture, $"Mind the Gaps scan finished: {gaps.Count} gaps ({newCount} new)."),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["totalGaps"] = gaps.Count,
                ["newGaps"] = newCount,
                ["generatedUtc"] = report.GeneratedUtc
            },
            cancellationToken).ConfigureAwait(false);

        return report;
    }

    // A recommendation target can be surfaced by several owned titles, but they collapse to one gap (the
    // id is keyed on the target). Instead of dropping the duplicates, accumulate their owning items onto
    // the surviving gap so the report can list every recommending source. Capped and de-duped.
    private static void MergeDuplicateSource(GapItem existing, GapItem duplicate)
    {
        const int maxSourcesPerGap = 12;

        if (existing.Pattern != GapPattern.Recommendation || string.IsNullOrEmpty(duplicate.SourceItemName))
        {
            return;
        }

        if (string.Equals(duplicate.SourceItemName, existing.SourceItemName, StringComparison.Ordinal)
            && string.Equals(duplicate.SourceItemId, existing.SourceItemId, StringComparison.Ordinal))
        {
            return;
        }

        if (existing.OtherSources.Count >= maxSourcesPerGap)
        {
            return;
        }

        var key = duplicate.SourceItemId ?? duplicate.SourceItemName;
        foreach (var existingSource in existing.OtherSources)
        {
            if (string.Equals(existingSource.Id ?? existingSource.Name, key, StringComparison.Ordinal))
            {
                return;
            }
        }

        var sources = new List<GapSourceRef>(existing.OtherSources)
        {
            new GapSourceRef
            {
                Id = duplicate.SourceItemId,
                Name = duplicate.SourceItemName,
                Type = duplicate.SourceItemType,
                Year = duplicate.SourceItemYear
            }
        };
        existing.OtherSources = sources;
    }

    private void CarryForward(IReadOnlyList<GapItem> gaps)
    {
        var prior = _store.Load().Items;
        if (prior.Count == 0)
        {
            return;
        }

        var priorById = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        foreach (var item in prior)
        {
            priorById[item.Id] = item;
        }

        foreach (var gap in gaps)
        {
            if (!priorById.TryGetValue(gap.Id, out var before))
            {
                continue;
            }

            // Re-adopt any external ids the background pass resolved last time (the sources only stamp
            // a TMDB id), and rebuild the fallback links the added ids imply.
            var merged = new Dictionary<string, string>(gap.ProviderIds, StringComparer.OrdinalIgnoreCase);
            var added = false;
            foreach (var pair in before.ProviderIds)
            {
                if (!string.IsNullOrEmpty(pair.Value) && !merged.ContainsKey(pair.Key))
                {
                    merged[pair.Key] = pair.Value;
                    added = true;
                }
            }

            if (added)
            {
                gap.ProviderIds = merged;
                gap.Links = ExternalLinkEnricher.Merge(gap.Links, ProviderLinks.Build(gap.TargetKind, merged));
            }

            // Carry the episode's watch target (its series' TMDB id, resolved by an earlier pass) so a
            // rescan does not drop it and re-resolve; sources only stamp it when they can.
            if (string.IsNullOrEmpty(gap.WatchTmdbId) && !string.IsNullOrEmpty(before.WatchTmdbId))
            {
                gap.WatchTmdbId = before.WatchTmdbId;
            }

            if (before.AvailabilityChecked)
            {
                gap.AvailabilityChecked = true;
            }

            if (gap.Availability.Count == 0 && before.Availability.Count > 0)
            {
                gap.Availability = before.Availability;
            }
        }
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
