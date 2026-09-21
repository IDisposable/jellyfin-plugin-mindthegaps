using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class GenerationMemoTests
{
    [Fact]
    public void ComputesOncePerGeneration()
    {
        var memo = new GenerationMemo<int>();
        var computed = 0;

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(70, memo.GetOrCompute(7, () =>
            {
                computed++;
                return 70;
            }));
        }

        Assert.Equal(1, computed);
    }

    [Fact]
    public void TryGet_HoldsOnlyTheGenerationItWasComputedFor()
    {
        var memo = new GenerationMemo<int>();

        Assert.False(memo.TryGet(3, out _));

        memo.GetOrCompute(3, () => 30);

        Assert.True(memo.TryGet(3, out var value));
        Assert.Equal(30, value);
        Assert.False(memo.TryGet(4, out _));
        Assert.False(memo.TryGet(2, out _));
    }

    [Fact]
    public void RecomputesWhenTheGenerationMoves()
    {
        var memo = new GenerationMemo<int>();

        Assert.Equal(10, memo.GetOrCompute(1, () => 10));
        Assert.Equal(20, memo.GetOrCompute(2, () => 20));
    }

    [Fact]
    public void ASlowCallerWithAnOlderGeneration_DoesNotReplaceANewerResult()
    {
        var memo = new GenerationMemo<int>();
        var computed = 0;
        int Compute(int value)
        {
            computed++;
            return value;
        }

        memo.GetOrCompute(6, () => Compute(60));

        // The stale caller still gets the value for the generation it read...
        Assert.Equal(50, memo.GetOrCompute(5, () => Compute(50)));

        // ...and the memo still holds generation 6, so it is not recomputed.
        Assert.Equal(60, memo.GetOrCompute(6, () => Compute(-1)));
        Assert.Equal(2, computed);
    }

    [Fact]
    public void ANewerResultPublishedWhileThisOneWasComputing_IsKept()
    {
        var memo = new GenerationMemo<int>();

        // While generation 5 is being computed, another caller computes and publishes generation 6.
        var five = memo.GetOrCompute(5, () =>
        {
            memo.GetOrCompute(6, () => 60);
            return 50;
        });

        Assert.Equal(50, five);
        var recomputed = false;
        Assert.Equal(60, memo.GetOrCompute(6, () =>
        {
            recomputed = true;
            return -1;
        }));
        Assert.False(recomputed);
    }

    [Fact]
    public void AnOlderResultPublishedWhileThisOneWasComputing_IsReplaced()
    {
        var memo = new GenerationMemo<int>();

        // While generation 6 is being computed, another caller publishes the older generation 5 first.
        var six = memo.GetOrCompute(6, () =>
        {
            memo.GetOrCompute(5, () => 50);
            return 60;
        });

        Assert.Equal(60, six);
        var recomputed = false;
        Assert.Equal(60, memo.GetOrCompute(6, () =>
        {
            recomputed = true;
            return -1;
        }));
        Assert.False(recomputed);
    }

    [Fact]
    public void ManyThreadsRacingAcrossGenerations_NeverSeeAValueForTheWrongGeneration_AndSettleOnTheNewest()
    {
        var memo = new GenerationMemo<long>();
        var wrong = 0;

        Parallel.For(1, 2001, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var generation = (i % 200) + 1;
            var value = memo.GetOrCompute(generation, () => generation * 10L);
            if (value != generation * 10L)
            {
                Interlocked.Increment(ref wrong);
            }
        });

        Assert.Equal(0, wrong);

        // Whatever order the threads finished in, the memo ends holding the newest generation seen.
        var recomputed = false;
        Assert.Equal(2000, memo.GetOrCompute(200, () =>
        {
            recomputed = true;
            return -1;
        }));
        Assert.False(recomputed);
    }
}
