using System;
using System.Diagnostics;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Paces the report writes nothing waits on (a scan's checkpoints, an availability pass's saves, a bulk re-check's
/// swaps): a write is due once the longer of <see cref="Floor"/> and <see cref="DutyFactor"/> times the last write
/// has passed. On a large report a whole write takes about a second, so a pace of a few seconds would spend a fifth of a
/// scan writing; this keeps writing under a twentieth of the time on any report or disk. A write these pace only saves
/// work: what a crash between two of them loses is found again by the next run, which the caller's own forced
/// writes (a source finishing, the end of a batch) are there to keep true. Not thread-safe; the caller serializes.
/// </summary>
internal sealed class WriteThrottle
{
    /// <summary>
    /// How many times the last write's duration must pass before the next.
    /// </summary>
    public const int DutyFactor = 20;

    /// <summary>
    /// The shortest time between two paced writes.
    /// </summary>
    public static readonly TimeSpan Floor = TimeSpan.FromMinutes(2);

    private readonly TimeProvider _clock;
    private DateTimeOffset _last;
    private TimeSpan _lastDuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="WriteThrottle"/> class, counting from now.
    /// </summary>
    /// <param name="clock">The clock.</param>
    public WriteThrottle(TimeProvider clock)
    {
        _clock = clock;
        _last = clock.GetUtcNow();
    }

    /// <summary>
    /// Gets how long after the last write the next is due.
    /// </summary>
    public TimeSpan Interval
    {
        get
        {
            var paced = _lastDuration * DutyFactor;
            return paced > Floor ? paced : Floor;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a paced write is due.
    /// </summary>
    public bool IsDue => _clock.GetUtcNow() - _last >= Interval;

    /// <summary>
    /// Runs a write and records when it ended and how long it took. Forced writes go through here too, since
    /// any write resets the wait.
    /// </summary>
    /// <param name="write">The write.</param>
    public void Run(Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var started = Stopwatch.GetTimestamp();
        try
        {
            write();
        }
        finally
        {
            Wrote(Stopwatch.GetElapsedTime(started));
        }
    }

    /// <summary>
    /// Records a write that just ended.
    /// </summary>
    /// <param name="duration">How long it took.</param>
    public void Wrote(TimeSpan duration)
    {
        _last = _clock.GetUtcNow();
        _lastDuration = duration;
    }
}
