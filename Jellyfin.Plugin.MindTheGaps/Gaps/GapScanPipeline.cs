using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Runs every enabled <see cref="IGapSource"/> concurrently for one scan, merges what they produce as it
/// arrives, and checkpoints the in-progress result to <see cref="GapStore"/>. Split out of
/// <see cref="GapEngine"/> because this is the one genuinely concurrent, streaming piece of a scan; the
/// backfill and re-check passes that follow it in <see cref="GapEngine.RunAsync"/> are all simple sequential
/// passes over the finished list.
/// </summary>
public sealed class GapScanPipeline
{
    private readonly GapStore _store;
    private readonly ILogger<GapScanPipeline> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapScanPipeline"/> class.
    /// </summary>
    /// <param name="store">The gap store, for mid-scan checkpoints.</param>
    /// <param name="logger">The logger.</param>
    public GapScanPipeline(GapStore store, ILogger<GapScanPipeline> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Runs the given sources concurrently and returns the merged, de-duplicated result.
    /// </summary>
    /// <param name="enabled">The enabled sources to run.</param>
    /// <param name="config">The scan's configuration (shared read-only across every source).</param>
    /// <param name="ownership">The scan's ownership index (shared read-only across every source).</param>
    /// <param name="priorReport">The previous report, whose gaps and metadata seed each mid-scan checkpoint.</param>
    /// <param name="progress">Optional progress reporter (0-100).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The merged gaps, plus each discovery kind's <see cref="SourceRun"/>.</returns>
    public async Task<GapScanPipelineResult> RunAsync(
        IReadOnlyList<IGapSource> enabled,
        PluginConfiguration config,
        OwnershipIndex ownership,
        GapReport priorReport,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(priorReport);

        var stopwatch = Stopwatch.StartNew();
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
                Items = merged.Values.ToList(),

                // The run in progress has not finished telling us what it read, so a checkpoint keeps the
                // last completed scan's account rather than blanking the Discover sections mid-scan.
                SourceRuns = priorReport.SourceRuns
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
        // read-only. De-dup is order-tolerant (GapSourceMerge only unions recommendation source-refs, which
        // come from a single source), so the streamed, completion-order merge is fine.
        var fractions = new double[Math.Max(1, total)];

        // Per slot, so each producer writes its own and no lock is needed.
        var runs = new SourceRun?[Math.Max(1, total)];
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
            var sourceContext = new GapScanContext(config, ownership);
            sourceContext.SetProgressSink(f =>
            {
                fractions[slot] = Math.Clamp(f, 0.0, 1.0);
                ReportAggregate();
            });

            var produced = 0;
            var started = Stopwatch.GetTimestamp();

            // A discovery source's own kind is counted separately: one source can straddle both patterns
            // (curated sets emit studios and keywords as well as TMDB lists), and what the Discover tab
            // needs to know is how many gaps arrived for the section, not how many the source produced.
            var discovered = 0;
            var discoverKind = (source as IDiscoverSource)?.DiscoverKind;
            var failed = false;
            try
            {
                await foreach (var gap in source.FindGapsAsync(sourceContext, cancellationToken).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(gap, cancellationToken).ConfigureAwait(false);
                    produced++;
                    if (discoverKind is not null && string.Equals(gap.SourceItemType, discoverKind, StringComparison.Ordinal))
                    {
                        discovered++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed = true;
                _logger.LogError(ex, "Gap source {Source} failed", source.Name);
            }
            finally
            {
                fractions[slot] = 1.0;
                ReportAggregate();
                if (discoverKind is not null)
                {
                    runs[slot] = new SourceRun { Kind = discoverKind, Name = source.Name, Gaps = discovered, Failed = failed };
                }

                // The sources run concurrently, so the scan ends with the slowest one and the progress bar
                // creeps once only the rate-paced sources are left. The time names which one held it up.
                _logger.LogInformation("Gap source {Source} produced {Count} gaps in {Seconds:F0}s", source.Name, produced, Stopwatch.GetElapsedTime(started).TotalSeconds);
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
                    GapSourceMerge.Merge(existing, gap);
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

        return new GapScanPipelineResult(gaps, runs.Where(r => r is not null).Select(r => r!).ToList());
    }
}
