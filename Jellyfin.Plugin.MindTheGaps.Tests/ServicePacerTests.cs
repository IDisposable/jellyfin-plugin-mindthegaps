using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Shares the process-wide pacing state, so it runs in the same non-parallel collection as the other
// HTTP-infrastructure tests (see ServiceCircuitTests for the collection definition).
[Collection("ServiceCircuit")]
public class ServicePacerTests
{
    public ServicePacerTests() => ServicePacer.ResetAll();

    [Fact]
    public void MusicBrainz_IsPacedToAtMostOnePerSecond()
        => Assert.True(ServicePacer.MinIntervalMs("MusicBrainz") >= 1000);

    [Fact]
    public void UnconfiguredService_IsNotPaced()
        => Assert.Equal(0, ServicePacer.MinIntervalMs("TheTVDB"));

    [Fact]
    public async Task WaitAsync_UnpacedService_DoesNotDelay()
    {
        var sw = Stopwatch.StartNew();
        await ServicePacer.WaitAsync("TheTVDB", CancellationToken.None);
        await ServicePacer.WaitAsync("TheTVDB", CancellationToken.None);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50, $"unpaced calls should be immediate, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task WaitAsync_PacedService_SpacesConsecutiveCalls()
    {
        // A short test interval so the spacing is observable without a real one-second wait.
        ServicePacer.SetIntervalForTest("PacerTestSvc", 150);

        var sw = Stopwatch.StartNew();
        await ServicePacer.WaitAsync("PacerTestSvc", CancellationToken.None); // first: immediate
        var afterFirst = sw.ElapsedMilliseconds;
        await ServicePacer.WaitAsync("PacerTestSvc", CancellationToken.None); // second: waits out the interval
        sw.Stop();

        Assert.True(afterFirst < 50, $"first call should be immediate, took {afterFirst}ms");
        Assert.True(sw.ElapsedMilliseconds >= 130, $"second call should wait the interval, total was {sw.ElapsedMilliseconds}ms");
    }
}
