using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MindTheGaps.Services.Http;

/// <summary>
/// A tiny per-service request pacer shared by the hand-rolled clients via <see cref="HttpRetry"/>. Some APIs
/// publish a steady-state rate limit (MusicBrainz asks for no more than one request per second), which the
/// retry/backoff in <see cref="HttpRetry"/> and the <see cref="ServiceCircuit"/> only address reactively,
/// after a 429 has already come back. This spaces requests to such a service proactively: a call to a paced
/// service waits until at least its minimum interval has passed since the previous one, so the limit is not
/// tripped in the first place. A service with no configured interval passes straight through. Process-wide,
/// since one scan runs at a time.
/// </summary>
internal static class ServicePacer
{
    // Minimum milliseconds between requests, per service. MusicBrainz asks for at most one request per
    // second, and Discogs allows 60 authenticated requests a minute (one a second); the small margin over
    // 1000 ms keeps a burst safely under both. Other services pass through.
    private static readonly ConcurrentDictionary<string, int> _minIntervalMs = new(StringComparer.Ordinal)
    {
        ["MusicBrainz"] = 1100,
        ["Discogs"] = 1100
    };

    private static readonly ConcurrentDictionary<string, Gate> _gates = new(StringComparer.Ordinal);

    /// <summary>
    /// Waits until the configured minimum interval has passed since the previous request to the service, then
    /// records this request as the most recent. A no-op for a service with no configured interval.
    /// </summary>
    /// <param name="service">The service name (the same one passed to <see cref="HttpRetry"/>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once it is this caller's turn.</returns>
    public static Task WaitAsync(string service, CancellationToken cancellationToken)
    {
        if (!_minIntervalMs.TryGetValue(service, out var intervalMs) || intervalMs <= 0)
        {
            return Task.CompletedTask;
        }

        return _gates.GetOrAdd(service, _ => new Gate(intervalMs)).WaitAsync(cancellationToken);
    }

    // The configured minimum interval for a service (0 when unpaced). Exposed for tests.
    internal static int MinIntervalMs(string service)
        => _minIntervalMs.TryGetValue(service, out var ms) ? ms : 0;

    // Register or override a service's interval. For tests, so a fast interval can exercise the spacing.
    internal static void SetIntervalForTest(string service, int intervalMs)
        => _minIntervalMs[service] = intervalMs;

    // Forget all pacing (timing) state, so a test starts from a clean slate.
    internal static void ResetAll()
    {
        foreach (var gate in _gates.Values)
        {
            gate.Dispose();
        }

        _gates.Clear();
    }

    private sealed class Gate : IDisposable
    {
        private readonly SemaphoreSlim _turnstile = new(1, 1);
        private readonly int _intervalMs;
        private long _nextAllowedTick;

        public Gate(int intervalMs) => _intervalMs = intervalMs;

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            // Serialize callers to this service and release them no faster than one per interval: each takes
            // the turnstile, waits out any remaining time, stamps the next slot, then frees the next caller.
            await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = Environment.TickCount64;
                var wait = _nextAllowedTick - now;
                if (wait > 0)
                {
                    await Task.Delay((int)wait, cancellationToken).ConfigureAwait(false);
                    now = Environment.TickCount64;
                }

                _nextAllowedTick = now + _intervalMs;
            }
            finally
            {
                _turnstile.Release();
            }
        }

        public void Dispose() => _turnstile.Dispose();
    }
}
