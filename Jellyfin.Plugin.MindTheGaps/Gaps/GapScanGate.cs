using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Lets one full scan, or one report-wide edit that a scan would undo, run at a time. A scan reads the report
/// when it starts and saves a whole new one built from that read when it ends, so an edit that lands in
/// between is overwritten; holding this across both keeps the edit from landing there. A second scan waits
/// too, rather than two scans building from the same read.
/// </summary>
public sealed class GapScanGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Waits until nothing else holds the gate, then holds it until the returned handle is disposed.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The handle that releases the gate.</returns>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_gate);
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
