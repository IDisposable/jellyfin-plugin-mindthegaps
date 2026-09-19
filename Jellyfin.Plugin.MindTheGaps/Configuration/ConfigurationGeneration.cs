using System;
using System.Threading;

namespace Jellyfin.Plugin.MindTheGaps.Configuration;

/// <summary>
/// Counts saves of the plugin configuration, so a response that depends on it (the summary's enabled
/// sources and availability switch) can be validated against a cached copy without recomputing it.
/// Bumped from <see cref="Plugin"/>'s save, which every write path goes through.
/// </summary>
internal static class ConfigurationGeneration
{
    private static long _value;
    private static long _lastChangedTicks;

    /// <summary>
    /// Gets the current generation.
    /// </summary>
    public static long Value => Interlocked.Read(ref _value);

    /// <summary>
    /// Gets the UTC time of the last save, or <see cref="DateTime.MinValue"/> when there has been none this run.
    /// </summary>
    public static DateTime LastChangedUtc => new(Interlocked.Read(ref _lastChangedTicks), DateTimeKind.Utc);

    /// <summary>
    /// Records that the configuration was saved.
    /// </summary>
    public static void Bump()
    {
        // The time is written first and only ever forward, so racing saves cannot leave an earlier time
        // stored after a later one, and a reader that sees a generation never pairs it with an older time.
        var now = DateTime.UtcNow.Ticks;
        long seen;
        while ((seen = Interlocked.Read(ref _lastChangedTicks)) < now
            && Interlocked.CompareExchange(ref _lastChangedTicks, now, seen) != seen)
        {
        }

        Interlocked.Increment(ref _value);
    }
}
