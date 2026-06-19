using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// ServiceCircuit and HttpRetry share process-wide circuit state, so the tests that touch it run in one
// non-parallel collection: otherwise two classes would mutate the same static state at once.
[CollectionDefinition("ServiceCircuit", DisableParallelization = true)]
public sealed class ServiceCircuitCollection
{
}

[Collection("ServiceCircuit")]
public class ServiceCircuitTests
{
    public ServiceCircuitTests()
    {
        ServiceCircuit.ResetAll();
        ServiceCircuit.OnTrip = null;
    }

    [Fact]
    public void OpensAfterFiveConsecutiveGiveUps()
    {
        for (var i = 0; i < 4; i++)
        {
            Assert.False(ServiceCircuit.RecordFailure("svc"), "should not trip before the threshold");
        }

        Assert.False(ServiceCircuit.IsOpen("svc"));
        Assert.True(ServiceCircuit.RecordFailure("svc"), "the fifth failure trips it");
        Assert.True(ServiceCircuit.IsOpen("svc"));
    }

    [Fact]
    public void ASuccessResetsTheStreak()
    {
        for (var i = 0; i < 4; i++)
        {
            ServiceCircuit.RecordFailure("svc");
        }

        ServiceCircuit.RecordSuccess("svc");

        for (var i = 0; i < 4; i++)
        {
            Assert.False(ServiceCircuit.RecordFailure("svc"));
        }

        Assert.False(ServiceCircuit.IsOpen("svc"));
    }

    [Fact]
    public void OnTripFiresOnceWhenItOpens()
    {
        var tripped = new List<string>();
        ServiceCircuit.OnTrip = s => tripped.Add(s);

        for (var i = 0; i < 8; i++)
        {
            ServiceCircuit.RecordFailure("svc");
        }

        // The open transition fires once; the extra failures while open do not re-fire.
        Assert.Equal(new[] { "svc" }, tripped);
    }

    [Fact]
    public void TracksEachServiceIndependently()
    {
        for (var i = 0; i < 5; i++)
        {
            ServiceCircuit.RecordFailure("a");
        }

        Assert.True(ServiceCircuit.IsOpen("a"));
        Assert.False(ServiceCircuit.IsOpen("b"));
    }
}
