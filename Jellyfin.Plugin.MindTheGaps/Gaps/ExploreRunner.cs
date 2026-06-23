using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Runs an ad-hoc "explore a source" pass in the background so a slow provider does not block (and time
/// out) the HTTP request that triggers it. It explores one by-id source kind (a TMDB studio, keyword, or
/// list, a Discogs label, or an MDBList list) for the explicit ids the user picked: it runs that source
/// against current ownership, marks the produced gaps ad-hoc, and merges them additively into the
/// persisted report without a full rescan. Only one explore runs at a time; a second request while one is
/// running is a no-op.
/// </summary>
public sealed class ExploreRunner
{
    private readonly GapEngine _engine;
    private readonly PluginLifetime _lifetime;
    private readonly GapStore _store;
    private readonly ILogger<ExploreRunner> _logger;
    private readonly object _lock = new();
    // The run claim is a lock-free flag (0 = idle, 1 = running): TryStartExplore claims it with a single
    // atomic compare-and-set. The remaining status fields stay under _lock so a status read is one
    // consistent snapshot (and _progress is a double, which is not guaranteed atomic without it).
    private int _running;
    private double _progress;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExploreRunner"/> class.
    /// </summary>
    /// <param name="engine">The gap engine, which routes the explore kind to its source.</param>
    /// <param name="lifetime">The plugin-lifetime cancellation, so an explore stops on shutdown.</param>
    /// <param name="store">The gap store, into which the result is merged additively.</param>
    /// <param name="logger">The logger.</param>
    public ExploreRunner(GapEngine engine, PluginLifetime lifetime, GapStore store, ILogger<ExploreRunner> logger)
    {
        _engine = engine;
        _lifetime = lifetime;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether an explore is currently running.
    /// </summary>
    public bool IsExploring => Volatile.Read(ref _running) != 0;

    /// <summary>
    /// Gets the progress (0-100) of the running explore.
    /// </summary>
    public double Progress
    {
        get
        {
            lock (_lock)
            {
                return _progress;
            }
        }
    }

    /// <summary>
    /// Starts an ad-hoc explore of one by-id source kind for the given ids in the background if one is not
    /// already running and the kind is supported.
    /// </summary>
    /// <param name="kind">The explore kind: one of <see cref="GapEngine.ExploreKinds"/>.</param>
    /// <param name="ids">The ids to explore (a studio/keyword/list/label/list id, depending on the kind).</param>
    /// <returns><see langword="true"/> if this call started an explore; <see langword="false"/> if one was already running, the kind is unsupported, or no ids were given.</returns>
    public bool TryStartExplore(string kind, IReadOnlyList<int> ids)
    {
        if (!GapEngine.IsExploreKind(kind) || ids is null || ids.Count == 0)
        {
            return false;
        }

        // Claim the runner atomically; if it was already running, do nothing.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return false;
        }

        lock (_lock)
        {
            _progress = 0;
        }

        // Snapshot the ids so a caller cannot mutate the list after we start.
        var picked = ids.ToList();

        // Detached from any HTTP request token on purpose: the explore must outlive the request that
        // started it. It observes the plugin-lifetime token so it stops on shutdown.
        _ = Task.Run(() => RunAsync(kind, picked));
        return true;
    }

    private async Task RunAsync(string kind, IReadOnlyList<int> ids)
    {
        try
        {
            _logger.LogInformation(
                "Background explore started for {Kind} id(s) {Ids}",
                kind,
                string.Join(", ", ids.Select(id => id.ToString(CultureInfo.InvariantCulture))));
            var progress = new Progress<double>(p =>
            {
                lock (_lock)
                {
                    _progress = p;
                }
            });

            var report = await _engine.RunExploreAsync(kind, ids, progress, _lifetime.Stopping).ConfigureAwait(false);
            var added = _store.MergeAdditiveGaps(report);
            _logger.LogInformation(
                "Background explore finished for {Kind}: {Found} gaps found, {Added} new",
                kind,
                report.Items.Count,
                added);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Background explore cancelled (plugin shutting down)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background explore failed for {Kind}", kind);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
