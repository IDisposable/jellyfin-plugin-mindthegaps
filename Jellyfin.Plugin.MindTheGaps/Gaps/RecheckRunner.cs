using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Runs a bulk re-check in the background so a heading with many sets under it (every studio, every
/// collection) does not block, and time out, the HTTP request that triggers it. Each owning item is swapped
/// into the report as it finishes, so progress survives a cancelled run. Only one bulk re-check runs at a
/// time; a second request while one is running is a no-op.
/// </summary>
public sealed class RecheckRunner
{
    private readonly GapEngine _engine;
    private readonly PluginLifetime _lifetime;
    private readonly ILogger<RecheckRunner> _logger;
    private readonly object _lock = new();
    // The run claim is a lock-free flag (0 = idle, 1 = running), claimed with a single atomic
    // compare-and-set; the rest of the status stays under _lock so a read is one consistent snapshot.
    private int _running;
    private double _progress;
    private int _total;
    private int _done;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecheckRunner"/> class.
    /// </summary>
    /// <param name="engine">The gap engine, which re-checks the batch.</param>
    /// <param name="lifetime">The plugin-lifetime cancellation, so a run stops on shutdown.</param>
    /// <param name="logger">The logger.</param>
    public RecheckRunner(GapEngine engine, PluginLifetime lifetime, ILogger<RecheckRunner> logger)
    {
        _engine = engine;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether a bulk re-check is currently running.
    /// </summary>
    public bool IsRunning => Volatile.Read(ref _running) != 0;

    /// <summary>
    /// Gets the progress (0-100) of the running re-check.
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
    /// Gets how many owning items the running (or last) re-check was asked to cover.
    /// </summary>
    public int Total
    {
        get
        {
            lock (_lock)
            {
                return _total;
            }
        }
    }

    /// <summary>
    /// Gets how many owning items the running (or last) re-check has finished.
    /// </summary>
    public int Done
    {
        get
        {
            lock (_lock)
            {
                return _done;
            }
        }
    }

    /// <summary>
    /// Starts a bulk re-check of the given owning items in the background, if one is not already running.
    /// </summary>
    /// <param name="ownerIds">The owning library items to re-check.</param>
    /// <returns><see langword="true"/> if this call started a run; <see langword="false"/> if one was already running or no ids were given.</returns>
    public bool TryStart(IReadOnlyList<Guid> ownerIds)
    {
        if (ownerIds is null || ownerIds.Count == 0)
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return false;
        }

        // Snapshot the ids so a caller cannot mutate the list after we start.
        var picked = ownerIds.ToList();

        lock (_lock)
        {
            _progress = 0;
            _total = picked.Count;
            _done = 0;
        }

        // Detached from the request token on purpose: the run must outlive the request that started it. It
        // observes the plugin-lifetime token so it stops on shutdown.
        _ = Task.Run(() => RunAsync(picked));
        return true;
    }

    private async Task RunAsync(IReadOnlyList<Guid> ownerIds)
    {
        try
        {
            _logger.LogInformation("Background re-check started for {Count} item(s)", ownerIds.Count);
            var progress = new Progress<double>(p =>
            {
                lock (_lock)
                {
                    _progress = p;
                    _done = (int)Math.Round(p / 100.0 * _total);
                }
            });

            var done = await _engine.RecheckManyAsync(ownerIds, progress, _lifetime.Stopping).ConfigureAwait(false);
            _logger.LogInformation("Background re-check finished: {Done} of {Asked} item(s)", done, ownerIds.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Background re-check cancelled (plugin shutting down)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background re-check failed");
        }
        finally
        {
            lock (_lock)
            {
                _progress = 100;
            }

            Volatile.Write(ref _running, 0);
        }
    }
}
