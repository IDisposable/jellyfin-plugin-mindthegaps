using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Runs a gap scan in the background so a large library does not block (and time out) the HTTP request
/// that triggers it. Only one scan runs at a time; a second request while one is running is a no-op.
/// </summary>
public sealed class GapScanRunner
{
    private readonly GapEngine _engine;
    private readonly ILogger<GapScanRunner> _logger;
    private readonly object _lock = new();
    private bool _running;
    private double _progress;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapScanRunner"/> class.
    /// </summary>
    /// <param name="engine">The gap engine.</param>
    /// <param name="logger">The logger.</param>
    public GapScanRunner(GapEngine engine, ILogger<GapScanRunner> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether a scan is currently running.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _running;
            }
        }
    }

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
        lock (_lock)
        {
            if (_running)
            {
                return false;
            }

            _running = true;
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

            await _engine.RunAsync(progress, CancellationToken.None).ConfigureAwait(false);
            _logger.LogInformation("Background gap scan finished");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background gap scan failed");
        }
        finally
        {
            lock (_lock)
            {
                _running = false;
            }
        }
    }
}
