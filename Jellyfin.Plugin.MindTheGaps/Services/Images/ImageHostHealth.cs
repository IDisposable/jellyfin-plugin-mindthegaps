using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.MindTheGaps.Services.Images;

/// <summary>
/// Remembers which image hosts have stopped answering the server, so the image route stops asking them and sends
/// the browser to the provider at once instead of waiting on a fetch that is going to fail. A host is blocked
/// after a run of failures that look like it refusing or dropping us (a 403 or 429, a server error, a
/// connection failure, a timeout); a missing image (404) or a file that is not an image is an answer, and
/// counts as the host being fine. The list lives in memory only, so a restart starts it empty. After the
/// cooldown one request is let through as a trial: it closes the block on success and reopens it on a failure.
/// </summary>
internal sealed class ImageHostHealth
{
    private const int FailureThreshold = 5;
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, State> _hosts = new(StringComparer.OrdinalIgnoreCase);

    public ImageHostHealth()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    public ImageHostHealth(Func<DateTimeOffset> now)
    {
        _now = now;
    }

    /// <summary>
    /// Whether the host is blocked, so a request to it should be sent straight to the provider.
    /// </summary>
    /// <param name="host">The host name.</param>
    /// <returns><see langword="true"/> while the host is blocked.</returns>
    public bool IsBlocked(string host)
        => _hosts.TryGetValue(host, out var state) && state.IsBlocked(_now());

    /// <summary>
    /// Records that the host answered, whatever the answer was, which clears its failures.
    /// </summary>
    /// <param name="host">The host name.</param>
    /// <returns><see langword="true"/> when this ended an outage that <see cref="Failed"/> had reported, so the
    /// caller can say the host is back.</returns>
    public bool Answered(string host)
        => _hosts.TryGetValue(host, out var state) && state.Reset();

    /// <summary>
    /// Records a failure that looks like the host refusing or dropping the server.
    /// </summary>
    /// <param name="host">The host name.</param>
    /// <returns><see langword="true"/> when this failure began an outage: the host was blocked, having answered
    /// since it was last. A failed trial after a cooldown re-blocks the host without beginning another one.</returns>
    public bool Failed(string host)
        => _hosts.GetOrAdd(host, _ => new State()).OnFailure(_now());

    private sealed class State
    {
        private readonly object _gate = new();
        private int _consecutiveFailures;
        private DateTimeOffset _blockedUntil;
        private bool _outageReported;

        public bool IsBlocked(DateTimeOffset now)
        {
            lock (_gate)
            {
                return _blockedUntil > now;
            }
        }

        // Returns true when this ends an outage that was reported.
        public bool Reset()
        {
            lock (_gate)
            {
                var wasReported = _outageReported;
                _consecutiveFailures = 0;
                _blockedUntil = default;
                _outageReported = false;
                return wasReported;
            }
        }

        // The count is not cleared when the cooldown ends, so the trial request that follows reopens the block
        // on a single failure. Returns true only for the failure that begins an outage, which is reported once
        // however many cooldowns it outlasts.
        public bool OnFailure(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_blockedUntil > now)
                {
                    return false;
                }

                _consecutiveFailures++;
                if (_consecutiveFailures < FailureThreshold)
                {
                    return false;
                }

                _blockedUntil = now + Cooldown;
                var begins = !_outageReported;
                _outageReported = true;
                return begins;
            }
        }
    }
}
