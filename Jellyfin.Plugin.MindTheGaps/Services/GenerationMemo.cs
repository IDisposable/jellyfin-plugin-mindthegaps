using System;
using System.Threading;

namespace Jellyfin.Plugin.MindTheGaps.Services;

/// <summary>
/// Remembers one value derived from something that changes in numbered generations, and recomputes it only
/// when the generation moves. Lock-free: the value and the generation it was computed for are one immutable
/// entry, so a reader can never pair a value with the wrong generation, and the entry only ever moves
/// forward, so a slow caller holding an older generation cannot replace a newer result.
/// </summary>
/// <typeparam name="T">The derived value.</typeparam>
internal sealed class GenerationMemo<T>
{
    private Entry? _entry;

    /// <summary>
    /// Gets the value for a generation, computing it when the memo does not hold that generation.
    /// </summary>
    /// <param name="generation">The generation the caller read.</param>
    /// <param name="compute">Computes the value for that generation. Concurrent callers may each run it.</param>
    /// <returns>The value for <paramref name="generation"/>, whatever the memo ends up holding.</returns>
    public T GetOrCompute(long generation, Func<T> compute)
    {
        ArgumentNullException.ThrowIfNull(compute);

        var cached = Volatile.Read(ref _entry);
        if (cached is not null && cached.Generation == generation)
        {
            return cached.Value;
        }

        var computed = new Entry(generation, compute());

        // Publish only over an entry that is older than this one. A failed exchange means another thread
        // published in the meantime: look at what it stored and try again only if that is still older.
        var current = cached;
        while (current is null || current.Generation < generation)
        {
            var seen = Interlocked.CompareExchange(ref _entry, computed, current);
            if (ReferenceEquals(seen, current))
            {
                break;
            }

            current = seen;
        }

        return computed.Value;
    }

    private sealed record Entry(long Generation, T Value);
}
