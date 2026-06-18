using System;
using System.Threading;

namespace Jellyfin.Plugin.MindTheGaps;

/// <summary>
/// Holds a cancellation token tied to the plugin's lifetime. The background runners (scan, mint,
/// availability) detach their work from the HTTP request that starts it, so without this a long task
/// would keep running after the plugin is unloaded or the server is shutting down. This is a DI
/// singleton; the host disposes it on shutdown, which cancels the token so the loops stop cooperatively.
/// </summary>
public sealed class PluginLifetime : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Gets a token that is cancelled when the plugin is being disposed (server shutdown or unload).
    /// Background loops should observe it so they stop promptly instead of running on detached.
    /// </summary>
    public CancellationToken Stopping => _cts.Token;

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed; nothing to cancel.
        }

        _cts.Dispose();
    }
}
