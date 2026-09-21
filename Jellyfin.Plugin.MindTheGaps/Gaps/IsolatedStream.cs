using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Runs one stream of results so its failure ends only that stream. A source that walks several
/// independent things (each list, keyword or studio the user configured) wraps each one, so a provider
/// answering "unauthorized" for one does not stop the ones after it.
/// </summary>
internal static class IsolatedStream
{
    /// <summary>
    /// Yields what the stream yields; if it throws, reports the exception and ends, keeping whatever it had
    /// already yielded. Cancellation is not a failure and is rethrown.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="stream">The stream to run.</param>
    /// <param name="onFailure">Called once with the exception when the stream fails.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stream's items, up to any failure.</returns>
    public static async IAsyncEnumerable<T> RunAsync<T>(
        IAsyncEnumerable<T> stream,
        Action<Exception> onFailure,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(onFailure);

        var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                // A yield cannot sit in a try with a catch, so only the advance is guarded and the item is
                // yielded after it.
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    onFailure(ex);
                    hasNext = false;
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }
}
