using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.VirtualItems;

/// <summary>
/// Runs a bulk mint/remove operation in the background so a large batch does not block (and time out)
/// the HTTP request that triggers it. Only one mint operation runs at a time; progress and the result
/// message are held for the caller to poll. Mirrors the gap scan's runner.
/// </summary>
public sealed class MintRunner
{
    private readonly PluginLifetime _lifetime;
    private readonly ILogger<MintRunner> _logger;
    private readonly object _lock = new();
    private bool _running;
    private double _progress;
    private string _lastMessage = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="MintRunner"/> class.
    /// </summary>
    /// <param name="lifetime">The plugin-lifetime cancellation, so a bulk mint stops on shutdown.</param>
    /// <param name="logger">The logger.</param>
    public MintRunner(PluginLifetime lifetime, ILogger<MintRunner> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether a mint operation is currently running.
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
    /// Gets the progress (0-100) of the running operation.
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
    /// Gets the message from the last completed operation.
    /// </summary>
    public string LastMessage
    {
        get
        {
            lock (_lock)
            {
                return _lastMessage;
            }
        }
    }

    /// <summary>
    /// Starts an operation in the background if one is not already running.
    /// </summary>
    /// <param name="work">The operation; it receives a progress sink (0-100) and returns a result message.</param>
    /// <returns><see langword="true"/> if this call started the operation; <see langword="false"/> if one was already running.</returns>
    public bool TryStart(Func<IProgress<double>, CancellationToken, Task<string>> work)
    {
        lock (_lock)
        {
            if (_running)
            {
                return false;
            }

            _running = true;
            _progress = 0;
            _lastMessage = string.Empty;
        }

        // Detached from the HTTP request token on purpose: the mint must outlive the request.
        _ = Task.Run(async () =>
        {
            var progress = new Progress<double>(p =>
            {
                lock (_lock)
                {
                    _progress = p;
                }
            });

            string message;
            try
            {
                message = await work(progress, _lifetime.Stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Background mint operation cancelled (plugin shutting down)");
                message = "Operation cancelled.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background mint operation failed");
                message = "Operation failed. Check the server logs.";
            }

            lock (_lock)
            {
                _running = false;
                _lastMessage = message;
            }
        });

        return true;
    }
}
