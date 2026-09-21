using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class IsolatedStreamTests
{
    private static async IAsyncEnumerable<int> Items(params int[] values)
    {
        foreach (var value in values)
        {
            yield return value;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<int> FailsAfter(int good, Exception failure)
    {
        for (var i = 0; i < good; i++)
        {
            yield return i;
            await Task.Yield();
        }

        throw failure;
    }

    private static async Task<List<int>> Drain(IAsyncEnumerable<int> stream)
    {
        var all = new List<int>();
        await foreach (var item in stream)
        {
            all.Add(item);
        }

        return all;
    }

    [Fact]
    public async Task ASuccessfulStream_PassesThroughAndReportsNothing()
    {
        var failures = new List<Exception>();

        var all = await Drain(IsolatedStream.RunAsync(Items(1, 2, 3), failures.Add, CancellationToken.None));

        Assert.Equal(new[] { 1, 2, 3 }, all);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task AStreamThatFailsBeforeItsFirstItem_YieldsNothingAndReportsTheFailure()
    {
        var failures = new List<Exception>();
        var boom = new UnauthorizedAccessException("private list");

        var all = await Drain(IsolatedStream.RunAsync(FailsAfter(0, boom), failures.Add, CancellationToken.None));

        Assert.Empty(all);
        Assert.Same(boom, Assert.Single(failures));
    }

    [Fact]
    public async Task AStreamThatFailsPartWay_KeepsWhatItAlreadyYielded()
    {
        var failures = new List<Exception>();

        var all = await Drain(IsolatedStream.RunAsync(FailsAfter(3, new InvalidOperationException()), failures.Add, CancellationToken.None));

        Assert.Equal(new[] { 0, 1, 2 }, all);
        Assert.Single(failures);
    }

    [Fact]
    public async Task ALaterStreamStillRuns_AfterAnEarlierOneFails()
    {
        var failures = new List<Exception>();

        var first = await Drain(IsolatedStream.RunAsync(FailsAfter(0, new UnauthorizedAccessException()), failures.Add, CancellationToken.None));
        var second = await Drain(IsolatedStream.RunAsync(Items(7, 8), failures.Add, CancellationToken.None));

        Assert.Empty(first);
        Assert.Equal(new[] { 7, 8 }, second);
        Assert.Single(failures);
    }

    [Fact]
    public async Task Cancellation_IsNotAFailure_AndPropagates()
    {
        var failures = new List<Exception>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Drain(IsolatedStream.RunAsync(FailsAfter(0, new OperationCanceledException()), failures.Add, CancellationToken.None)));

        Assert.Empty(failures);
    }

    [Fact]
    public async Task NullArguments_AreRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Drain(IsolatedStream.RunAsync<int>(null!, _ => { }, CancellationToken.None)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Drain(IsolatedStream.RunAsync(Items(1), null!, CancellationToken.None)));
    }
}
