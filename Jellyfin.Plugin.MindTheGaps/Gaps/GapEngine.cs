using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
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
    private readonly ExploreRegistry _explore;
    private readonly GapStore _store;
    private readonly ExternalLinkEnricher _externalLinks;
    private readonly Services.Webhook.WebhookNotifier _webhook;
    private readonly ResolutionStore _resolutions;
    private readonly ILogger<GapEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapEngine"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="sources">The registered gap sources.</param>
    /// <param name="explore">The explore-kind registry, for ad-hoc explore runs.</param>
    /// <param name="store">The gap store.</param>
    /// <param name="externalLinks">Folds the host's external-url providers into each gap's links.</param>
    /// <param name="webhook">Posts a completion notification, if a webhook is configured.</param>
    /// <param name="resolutions">Holds dismissals, including whole-creator dismissals not to carry forward.</param>
    /// <param name="logger">The logger.</param>
    public GapEngine(
        ILibraryManager libraryManager,
        IEnumerable<IGapSource> sources,
        ExploreRegistry explore,
        GapStore store,
        ExternalLinkEnricher externalLinks,
        Services.Webhook.WebhookNotifier webhook,
        ResolutionStore resolutions,
        ILogger<GapEngine> logger)
    {
        _libraryManager = libraryManager;
        _sources = sources;
        _explore = explore;
        _store = store;
        _externalLinks = externalLinks;
        _webhook = webhook;
        _resolutions = resolutions;
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
        var stopwatch = Stopwatch.StartNew();
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var enabled = _sources.Where(s => s.IsEnabled(config)).ToList();
        _logger.LogInformation(
            "Gap scan starting: {Count} of {Total} sources enabled [{Sources}]",
            enabled.Count,
            _sources.Count(),
            string.Join(", ", enabled.Select(s => s.Name)));
        var context = BuildContext(enabled, config);

        var priorReport = _store.Load();
        var priorIds = new HashSet<string>(priorReport.Items.Select(i => i.Id), StringComparer.Ordinal);

        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);

        var total = enabled.Count;

        // Persist progress mid-scan so a crash or shutdown does not lose the batch. A checkpoint is the prior
        // report overlaid with the fresh gaps found so far (so it never drops gaps the report already had),
        // written to disk only (the cache stays the prior report for carry-forward). It is throttled, except
        // when forced after each source or when a service's circuit trips (an out-of-band "we gave up" save).
        var lastCheckpoint = stopwatch.Elapsed;
        void Checkpoint(bool force)
        {
            if (!force && stopwatch.Elapsed - lastCheckpoint < TimeSpan.FromSeconds(5))
            {
                return;
            }

            lastCheckpoint = stopwatch.Elapsed;
            var merged = new Dictionary<string, GapItem>(StringComparer.Ordinal);
            foreach (var item in priorReport.Items)
            {
                merged[item.Id] = item;
            }

            foreach (var fresh in gaps)
            {
                merged[fresh.Id] = fresh;
            }

            _store.SaveCheckpoint(new GapReport
            {
                GeneratedUtc = priorReport.GeneratedUtc,
                GeneratedVersion = priorReport.GeneratedVersion,
                TotalGaps = merged.Count,
                Items = merged.Values.ToList()
            });
        }

        // Each scan starts with a clean circuit so a service given up on last run gets a fresh chance.
        ServiceCircuit.ResetAll();

        // When a service's circuit trips mid-scan, flush the gaps found so far out of band rather than waiting
        // on the throttle. The trip fires on whichever producer thread gave up, so it only flags the consumer
        // (which owns the gap list); the consumer takes the actual checkpoint on its next turn.
        var forceCheckpoint = 0;
        ServiceCircuit.OnTrip = _ => Interlocked.Exchange(ref forceCheckpoint, 1);

        // Run the sources concurrently so a slow, rate-paced provider (MusicBrainz, Discogs at one request a
        // second) does not hold up the fast ones: the scan takes about as long as the slowest service rather
        // than the sum of them all. Each source produces its gaps into a channel as it resolves each item, and
        // this thread is the single consumer that merges, de-dups, and checkpoints, so a gap lands in the
        // report within the checkpoint throttle of its item being resolved rather than waiting for the whole
        // source to finish. Safe because only the consumer touches the shared report state, same-service calls
        // still serialize through ServicePacer, and the cache/circuit and ownership index are thread-safe or
        // read-only. De-dup is order-tolerant (MergeDuplicateSource only unions recommendation source-refs,
        // which come from a single source), so the streamed, completion-order merge is fine.
        var fractions = new double[Math.Max(1, total)];
        void ReportAggregate()
        {
            double sum = 0;
            foreach (var f in fractions)
            {
                sum += f;
            }

            progress?.Report(sum / Math.Max(1, total) * 100.0);
        }

        var channel = Channel.CreateUnbounded<GapItem>(new UnboundedChannelOptions { SingleReader = true });

        async Task ProduceAsync(IGapSource source, int slot)
        {
            // Each source gets its own context (sharing the read-only config and ownership index) so its
            // progress reporting does not race the others'.
            var sourceContext = new GapScanContext(config, context.Ownership);
            sourceContext.SetProgressSink(f =>
            {
                fractions[slot] = Math.Clamp(f, 0.0, 1.0);
                ReportAggregate();
            });

            var produced = 0;
            try
            {
                await foreach (var gap in source.FindGapsAsync(sourceContext, cancellationToken).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(gap, cancellationToken).ConfigureAwait(false);
                    produced++;
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
                fractions[slot] = 1.0;
                ReportAggregate();
                _logger.LogInformation("Gap source {Source} produced {Count} gaps", source.Name, produced);
            }
        }

        var producers = enabled.Select((source, i) => ProduceAsync(source, i)).ToList();

        // Close the channel once every source has finished producing, so the consumer loop ends.
        async Task DrainProducersAsync()
        {
            try
            {
                await Task.WhenAll(producers).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }

        var draining = DrainProducersAsync();
        try
        {
            // Consume as each item resolves: merge, de-dup, and checkpoint (throttled) so the report grows
            // incrementally rather than in per-source batches.
            await foreach (var gap in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
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

                Checkpoint(force: Interlocked.Exchange(ref forceCheckpoint, 0) == 1);
            }

            // Flush the complete scan results before the (in-memory) enrichment phase.
            Checkpoint(force: true);
        }
        finally
        {
            // The producers are done, so no further trip can fire; stop forcing checkpoints for this run.
            ServiceCircuit.OnTrip = null;

            // Observe producer completion: propagates cancellation; per-source failures were already logged.
            await draining.ConfigureAwait(false);
        }

        // Carry the previous report's enrichment forward by id (resolved external ids and "where to
        // watch") so a rescan does not throw away what the background pass found; it only needs to look
        // up genuinely new gaps. Do this before the host link pass so carried ids produce their links.
        CarryForward(gaps);

        // Backfill: filmography and recommendations only scan a slice of their seeds each run, so carry
        // forward prior gaps of those patterns that were not re-emitted this run and are still unowned.
        // Coverage then accumulates across runs instead of the un-scanned seeds' gaps vanishing. Gated on
        // the relevant source being enabled, so disabling it lets the accumulation drain on the next scan.
        if (config.ScanPeople || config.TraktEnabled)
        {
            AccumulateUnowned(gaps, byId, priorReport.Items, context.Ownership, GapPattern.CreatorWorks, GapResolution.CreatorPrefix);
        }

        if (config.ScanRecommendations)
        {
            AccumulateUnowned(gaps, byId, priorReport.Items, context.Ownership, GapPattern.Recommendation, GapResolution.RecSourcePrefix);
        }

        // The TVmaze/TheTVDB cross-checks scan only a slice of resolvable series each run, so an episode
        // one of them found (that the library reader does not also mint as a virtual episode) would vanish
        // on a run that did not re-check its series. Carry those forward, draining a carried gap once its
        // series leaves the library or the episode lands on disk. Gated on a cross-check being enabled so
        // turning them off lets the accumulation drain.
        if (config.ScanSeries && (config.TvMazeEnabled || config.TvdbEnabled))
        {
            AccumulateSeriesContent(gaps, byId, priorReport.Items);
        }

        // The collection and discography sources scan everything each run rather than rotating a slice, so
        // they are carried forward only to survive a source that failed mid-scan (a TMDB or music-provider
        // blip that would otherwise blank a collection or discography from the saved report).
        AccumulateSetCompletion(gaps, byId, priorReport.Items, context.Ownership, config);

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
        _logger.LogInformation(
            "Gap scan complete: {Total} gaps ({New} new) saved in {Ms} ms",
            gaps.Count,
            newCount,
            stopwatch.ElapsedMilliseconds);

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

    /// <summary>
    /// Runs the source behind an explore kind ad-hoc against current ownership for an explicit set of ids,
    /// marks every gap it produces <see cref="GapItem.Adhoc"/>, and returns the report. This is the "explore
    /// a source" path: it does not accumulate un-scanned prior gaps, reconcile minted placeholders, or save
    /// (the caller merges the result additively), so it only ever surfaces this source's gaps for these ids.
    /// The ownership index is scoped to just that source's <see cref="IGapSource.OwnedKinds"/>. Supported
    /// kinds are those the registered sources declare (see <see cref="ExploreRegistry"/>).
    /// </summary>
    /// <param name="kind">The explore kind: "studio", "keyword", "tmdblist", "label", or "mdblist".</param>
    /// <param name="ids">The explicit descriptor ids to run the source for (for example MDBList list ids).</param>
    /// <param name="progress">Optional progress reporter (0-100).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A report of the ad-hoc gaps found.</returns>
    /// <exception cref="ArgumentException">The kind is not a supported explore kind.</exception>
    public async Task<GapReport> RunExploreAsync(string kind, IReadOnlyList<int> ids, IProgress<double>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(ids);

        var descriptor = _explore.Find(kind)
            ?? throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"'{kind}' is not a supported explore kind."),
                nameof(kind));

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var ownership = BuildOwnershipIndex(descriptor.Source.OwnedKinds.Distinct().ToArray());
        var context = new GapScanContext(config, ownership);
        context.SetProgressSink(f => progress?.Report(Math.Clamp(f, 0.0, 1.0) * 100.0));

        _logger.LogInformation("Ad-hoc explore: running {Kind} source {Source} for {Count} id(s)", kind, descriptor.Source.Name, ids.Count);

        // Each scan starts with a clean circuit so a service given up on last run gets a fresh chance.
        ServiceCircuit.ResetAll();

        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);

        await foreach (var gap in descriptor.Run(context, ids, ct).ConfigureAwait(false))
        {
            gap.Adhoc = true;
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

        // Re-adopt any external ids and "where to watch" the background pass resolved for these gaps before,
        // and rebuild the links those ids imply, so an explore run does not throw away that enrichment.
        CarryForward(gaps);

        // Let the host's external-url providers contribute links, as a full scan does.
        _externalLinks.Enrich(gaps);

        _logger.LogInformation("Ad-hoc explore: source {Source} produced {Count} gaps", descriptor.Source.Name, gaps.Count);

        return new GapReport
        {
            GeneratedUtc = DateTime.UtcNow,
            GeneratedVersion = Plugin.Instance?.Version?.ToString() ?? string.Empty,
            TotalGaps = gaps.Count,
            Items = gaps
        };
    }

    // Carry prior gaps of one capped pattern forward across scans so its coverage accumulates: a gap that
    // was found before, is still not owned, and was not re-found this run (its seed was not in this run's
    // batch) is kept rather than dropped. Gaps whose source the user dismissed wholesale (creator or
    // recommendation source, per dismissedPrefix) drain away. Bounded so a huge library cannot grow the
    // report without limit. The carried gap object keeps its prior enrichment (availability, resolved ids).
    private void AccumulateUnowned(List<GapItem> gaps, Dictionary<string, GapItem> byId, IReadOnlyList<GapItem> prior, OwnershipIndex ownership, GapPattern pattern, string dismissedPrefix)
    {
        const int maxAccumulated = 50000;

        var dismissed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in _resolutions.GetAll().Keys)
        {
            if (id.StartsWith(dismissedPrefix, StringComparison.Ordinal))
            {
                dismissed.Add(id[dismissedPrefix.Length..]);
            }
        }

        var count = gaps.Count(g => g.Pattern == pattern);
        var carried = 0;
        foreach (var item in prior)
        {
            // Ad-hoc "explore" gaps are deliberately not carried forward: a scheduled scan clears any
            // exploration the user did not keep in config (a kept source re-produces them as permanent).
            if (item.Pattern != pattern || byId.ContainsKey(item.Id) || item.Adhoc)
            {
                continue;
            }

            if (item.SourceItemId is not null && dismissed.Contains(item.SourceItemId))
            {
                continue;
            }

            if (ownership.OwnsAny(item.TargetKind, item.ProviderIds))
            {
                continue;
            }

            if (count >= maxAccumulated)
            {
                _logger.LogInformation("Backfill: reached the {Max} accumulated cap for {Pattern}; older gaps not carried", maxAccumulated, pattern);
                break;
            }

            byId[item.Id] = item;
            gaps.Add(item);
            count++;
            carried++;
        }

        if (carried > 0)
        {
            _logger.LogInformation("Backfill: carried {Carried} unowned {Pattern} gaps forward from the previous scan", carried, pattern);
        }
    }

    // Carry forward prior missing-episode gaps (SetCompletion, Episode) that no source re-emitted this run,
    // so a cross-check discovery survives runs that did not re-check its series. A carried gap drains when
    // its owning series is gone from the library, or the specific season/episode is now owned on disk.
    // Owned-episode sets are computed lazily, once per distinct series we actually consider.
    private void AccumulateSeriesContent(List<GapItem> gaps, Dictionary<string, GapItem> byId, IReadOnlyList<GapItem> prior)
    {
        const int maxAccumulated = 50000;

        var ownedBySeries = new Dictionary<Guid, HashSet<(int Season, int Number)>>();
        var seriesExists = new Dictionary<Guid, bool>();
        var carried = 0;

        foreach (var item in prior)
        {
            if (item.Pattern != GapPattern.SetCompletion
                || item.TargetKind != BaseItemKind.Episode
                || byId.ContainsKey(item.Id))
            {
                continue;
            }

            if (!SeriesGapKey.TryParseEpisode(item.Id, out var season, out var number)
                || item.SourceItemId is null
                || !Guid.TryParseExact(item.SourceItemId, "N", out var seriesId))
            {
                continue;
            }

            if (!seriesExists.TryGetValue(seriesId, out var exists))
            {
                exists = _libraryManager.GetItemById(seriesId) is not null;
                seriesExists[seriesId] = exists;
            }

            if (!exists)
            {
                continue;
            }

            if (!ownedBySeries.TryGetValue(seriesId, out var owned))
            {
                owned = OwnedEpisodeNumbers(seriesId);
                ownedBySeries[seriesId] = owned;
            }

            if (owned.Contains((season, number)))
            {
                continue;
            }

            if (carried >= maxAccumulated)
            {
                _logger.LogInformation("Backfill: reached the {Max} accumulated cap for series content; older gaps not carried", maxAccumulated);
                break;
            }

            byId[item.Id] = item;
            gaps.Add(item);
            carried++;
        }

        if (carried > 0)
        {
            _logger.LogInformation("Backfill: carried {Carried} unowned series-content gaps forward from the previous scan", carried);
        }
    }

    // Carry forward prior set-completion gaps (collections, discographies) that this run did not re-emit and
    // are still unowned, so a transient upstream failure mid-scan does not blank them from the saved report.
    // These sources scan everything each run, so a clean run re-emits the live set (nothing extra is carried)
    // and a later clean run drops anything truly resolved. Gated per domain on that domain's source still being
    // enabled, so turning a source off lets its accumulation drain. Episode set-completion gaps are excluded:
    // AccumulateSeriesContent carries those, checking the library on disk directly.
    private void AccumulateSetCompletion(List<GapItem> gaps, Dictionary<string, GapItem> byId, IReadOnlyList<GapItem> prior, OwnershipIndex ownership, PluginConfiguration config)
    {
        const int maxAccumulated = 50000;

        var domains = new HashSet<MediaDomain>();
        if (config.ScanCollections)
        {
            domains.Add(MediaDomain.Movies);
        }

        if (config.ScanMusic || config.ScanDiscogs)
        {
            domains.Add(MediaDomain.Music);
        }

        if (domains.Count == 0)
        {
            return;
        }

        var carried = 0;
        foreach (var item in prior)
        {
            if (item.Pattern != GapPattern.SetCompletion
                || item.TargetKind == BaseItemKind.Episode
                || item.Adhoc
                || byId.ContainsKey(item.Id)
                || !domains.Contains(item.Domain))
            {
                continue;
            }

            // Mirror how the sources decide ownership: a provider-id match, or for an album the artist-and-title
            // name key (a release the library holds under a different provider's id), so a now-owned item is not
            // wrongly resurrected.
            if (ownership.OwnsAny(item.TargetKind, item.ProviderIds)
                || (item.TargetKind == BaseItemKind.MusicAlbum && ownership.OwnsByName(item.TargetKind, item.SourceItemName, item.Name)))
            {
                continue;
            }

            if (carried >= maxAccumulated)
            {
                _logger.LogInformation("Backfill: reached the {Max} accumulated cap for set completion; older gaps not carried", maxAccumulated);
                break;
            }

            byId[item.Id] = item;
            gaps.Add(item);
            carried++;
        }

        if (carried > 0)
        {
            _logger.LogInformation("Backfill: carried {Carried} unowned set-completion gaps forward from the previous scan", carried);
        }
    }

    private HashSet<(int Season, int Number)> OwnedEpisodeNumbers(Guid seriesId)
    {
        var owned = new HashSet<(int Season, int Number)>();
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = new[] { seriesId },
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            if (item is Episode episode
                && episode.ParentIndexNumber is int s
                && episode.IndexNumber is int n)
            {
                // One file can span several episodes (S01E01-E02), so own every number in the span; otherwise
                // a carried cross-check gap for the later part never drains even though the file is on disk.
                var last = episode.IndexNumberEnd is int end && end > n ? end : n;
                for (var number = n; number <= last; number++)
                {
                    owned.Add((s, number));
                }
            }
        }

        return owned;
    }

    // Several sources can surface the same missing title, but they collapse to one gap (the id is keyed on
    // the target). Instead of dropping the duplicates, fold their sources onto the surviving gap so the report
    // can list every source; a curated list outranks a per-title recommendation for the primary, grouping
    // source. See GapSourceMerge.
    private static void MergeDuplicateSource(GapItem existing, GapItem duplicate)
        => GapSourceMerge.Merge(existing, duplicate);

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
        return new GapScanContext(config, BuildOwnershipIndex(kinds));
    }

    private OwnershipIndex BuildOwnershipIndex(BaseItemKind[] kinds)
    {
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

                // Also index an album by its artist-and-title name key, so a source whose ids do not overlap
                // the library's (a Discogs release against a MusicBrainz-tagged album) can still match by name.
                if (item is MusicAlbum album && !string.IsNullOrEmpty(album.Name))
                {
                    byKey[OwnershipIndex.MakeKey(kind, OwnershipIndex.NameKeyProvider, OwnershipIndex.NameKey(album.AlbumArtist, album.Name))] = item;
                }
            }
        }

        var ownership = new OwnershipIndex(byKey);
        _logger.LogInformation(
            "Ownership index: {Items} owned items, {Keys} provider-id keys (an item has several ids), across kinds [{Kinds}]. Series content (missing episodes) is scanned separately and is not in this index",
            itemCount,
            ownership.Count,
            string.Join(", ", kinds));

        return ownership;
    }
}
