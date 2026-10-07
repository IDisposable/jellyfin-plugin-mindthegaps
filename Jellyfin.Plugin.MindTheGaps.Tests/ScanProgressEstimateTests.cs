using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The sources run at the same time, so the bar should track the time the slowest one has left, not the
// average of the sources' fractions. The durations are the ones measured on a 4,000-item library: most sources
// under 30 seconds, filmography 110, series content 222.
public class ScanProgressEstimateTests
{
    private static readonly double[] _measured = [30, 110, 222];

    // Every source moving at its own steady pace, t seconds in.
    private static double[] At(double t) => _measured.Select(s => System.Math.Min(1, t / s)).ToArray();

    [Theory]
    [InlineData(30)]
    [InlineData(110)]
    [InlineData(200)]
    public void TracksTheSlowestSourceAtItsUsualPace(double t)
        => Assert.Equal(t / 222, ScanProgressEstimate.Combine(At(t), _measured), 6);

    // The unweighted average read 83% at 110 seconds, half way through the scan.
    [Fact]
    public void DoesNotReadMostlyDoneOnceTheFastSourcesFinish()
        => Assert.True(ScanProgressEstimate.Combine(At(110), _measured) < 0.55);

    [Fact]
    public void IsCompleteOnlyWhenEverySourceIs()
    {
        Assert.Equal(1, ScanProgressEstimate.Combine([1, 1, 1], _measured), 6);
        Assert.True(ScanProgressEstimate.Combine([1, 1, 0.99], _measured) < 1);
    }

    [Fact]
    public void NeverMovesBackwardsAsTheSourcesAdvance()
    {
        var previous = 0.0;
        for (var t = 0; t <= 222; t += 7)
        {
            var now = ScanProgressEstimate.Combine(At(t), _measured);
            Assert.True(now >= previous);
            previous = now;
        }
    }

    [Fact]
    public void FallsBackToTheAverageWithNoHistory()
    {
        Assert.Null(ScanProgressEstimate.Expected([null, null]));
        Assert.Equal(0.5, ScanProgressEstimate.Combine([1, 0], null), 6);
    }

    [Fact]
    public void GivesASourceWithNoHistoryTheMedianOfTheOthers()
        => Assert.Equal([30, 110, 222, 110], ScanProgressEstimate.Expected([30, 110, 222, null])!);

    [Fact]
    public void GivesAnInstantSourceALittleWeight()
        => Assert.Equal([1, 10], ScanProgressEstimate.Expected([0, 10])!);

    [Fact]
    public void ReadsZeroWithNoSources()
        => Assert.Equal(0, ScanProgressEstimate.Combine([], null));
}
