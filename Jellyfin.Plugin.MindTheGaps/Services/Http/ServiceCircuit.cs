using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.MindTheGaps.Services.Http;

/// <summary>
/// A tiny per-service circuit breaker shared by the hand-rolled clients via <see cref="HttpRetry"/>. When
/// one service (TheTVDB, Trakt, ...) gives up on enough requests in a row, its circuit opens and further
/// calls to it fast-fail for a short cooldown instead of each waiting through the retry/backoff. That lets
/// a scan move on past a service that is down or rate-limiting hard, rather than spending minutes retrying
/// it for every owned item, and the other services keep working. A single success closes it again.
/// Process-wide, since one scan runs at a time.
/// </summary>
internal static class ServiceCircuit
{
    // Consecutive give-ups (after HttpRetry's own retries) before a service's circuit opens.
    private const int FailureThreshold = 5;

    private static readonly TimeSpan _openDuration = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<string, State> _states = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets a callback invoked the moment a service's circuit opens (a "we give up" edge), so a
    /// caller can react, for example by checkpointing progress. The scan engine sets it for the duration of
    /// a run and clears it after. Process-wide; one scan runs at a time.
    /// </summary>
    public static Action<string>? OnTrip { get; set; }

    /// <summary>
    /// Gets how long a circuit stays open before the next call is allowed through (half-open).
    /// </summary>
    public static TimeSpan OpenDuration => _openDuration;

    /// <summary>
    /// Reports whether the named service's circuit is currently open (calls should be skipped).
    /// </summary>
    /// <param name="service">The service name.</param>
    /// <returns><see langword="true"/> if open.</returns>
    public static bool IsOpen(string service)
        => _states.TryGetValue(service, out var state) && state.IsOpen;

    /// <summary>
    /// Records a successful (or definitively answered, non-retryable) call, closing the circuit.
    /// </summary>
    /// <param name="service">The service name.</param>
    public static void RecordSuccess(string service)
    {
        if (_states.TryGetValue(service, out var state))
        {
            state.Reset();
        }
    }

    /// <summary>
    /// Records a give-up (a transient failure or a retryable status that survived all retries). Opens the
    /// circuit once the consecutive-failure threshold is reached.
    /// </summary>
    /// <param name="service">The service name.</param>
    /// <returns><see langword="true"/> if this call opened a circuit that was closed.</returns>
    public static bool RecordFailure(string service)
    {
        var state = _states.GetOrAdd(service, _ => new State());
        var tripped = state.OnFailure();
        if (tripped)
        {
            OnTrip?.Invoke(service);
        }

        return tripped;
    }

    // Forget all circuit state. The engine calls this at the start of a scan so circuits are per-run, and
    // the tests call it so one case cannot leak into another.
    internal static void ResetAll() => _states.Clear();

    private sealed class State
    {
        private readonly object _gate = new();
        private int _consecutiveFailures;
        private DateTimeOffset _openUntil;

        public bool IsOpen
        {
            get
            {
                lock (_gate)
                {
                    return _openUntil > DateTimeOffset.UtcNow;
                }
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _consecutiveFailures = 0;
                _openUntil = default;
            }
        }

        // Returns true when this failure transitioned the circuit into the open state.
        public bool OnFailure()
        {
            lock (_gate)
            {
                // While already open, a fast-failed call does not count; the cooldown decides when to retry.
                if (_openUntil > DateTimeOffset.UtcNow)
                {
                    return false;
                }

                _consecutiveFailures++;
                if (_consecutiveFailures < FailureThreshold)
                {
                    return false;
                }

                _openUntil = DateTimeOffset.UtcNow + _openDuration;
                return true;
            }
        }
    }
}
