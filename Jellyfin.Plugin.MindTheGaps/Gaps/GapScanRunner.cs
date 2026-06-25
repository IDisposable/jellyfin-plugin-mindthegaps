using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.VirtualItems;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Runs a gap scan in the background so a large library does not block (and time out) the HTTP request
/// that triggers it. Only one scan runs at a time; a second request while one is running is a no-op.
/// </summary>
public sealed class GapScanRunner
{
    private readonly GapEngine _engine;
    private readonly VirtualItemMinter _minter;
    private readonly PluginLifetime _lifetime;
    private readonly ILogger<GapScanRunner> _logger;
    private readonly object _lock = new();
    // The run claim is a lock-free flag (0 = idle, 1 = running): TryStart claims it with a single atomic
    // compare-and-set. The remaining status fields stay under _lock so a status read is one consistent
    // snapshot (and _progress is a double, which is not guaranteed atomic without it).
    private int _running;
    private double _progress;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapScanRunner"/> class.
    /// </summary>
    /// <param name="engine">The gap engine.</param>
    /// <param name="minter">The minter (reconciles minted placeholders now owned for real).</param>
    /// <param name="lifetime">The plugin-lifetime cancellation, so a scan stops on shutdown.</param>
    /// <param name="logger">The logger.</param>
    public GapScanRunner(GapEngine engine, VirtualItemMinter minter, PluginLifetime lifetime, ILogger<GapScanRunner> logger)
    {
        _engine = engine;
        _minter = minter;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether a scan is currently running.
    /// </summary>
    public bool IsRunning => Volatile.Read(ref _running) != 0;

    /// <summary>
    /// Gets the progress (0-100) of the running scan.
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
    /// Starts a scan in the background if one is not already running.
    /// </summary>
    /// <returns><see langword="true"/> if this call started a scan; <see langword="false"/> if one was already running.</returns>
    public bool TryStart()
    {
        // Claim the runner atomically; if it was already running, do nothing.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return false;
        }

        lock (_lock)
        {
            _progress = 0;
        }

        // Detached from any HTTP request token on purpose: the scan must outlive the request that
        // started it. The scheduled task remains the cancellable path.
        _ = Task.Run(RunAsync);
        return true;
    }

    private async Task RunAsync()
    {
        try
        {
            _logger.LogInformation("Background gap scan started");
            var progress = new Progress<double>(p =>
            {
                lock (_lock)
                {
                    _progress = p;
                }
            });

            await _engine.RunAsync(progress, _lifetime.Stopping).ConfigureAwait(false);
            _minter.ReconcileMinted();
            _logger.LogInformation("Background gap scan finished");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Background gap scan cancelled (plugin shutting down)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background gap scan failed");
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
