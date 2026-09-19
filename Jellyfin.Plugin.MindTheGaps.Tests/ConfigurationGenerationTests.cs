using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ConfigurationGenerationTests
{
    [Fact]
    public void Bump_CountsEveryCall_EvenWhenSavesRace()
    {
        var before = ConfigurationGeneration.Value;

        Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ => ConfigurationGeneration.Bump());

        Assert.Equal(before + 1000, ConfigurationGeneration.Value);
    }

    [Fact]
    public void Bump_NeverMovesTheChangeTimeBackward_WhileSavesRace()
    {
        ConfigurationGeneration.Bump();
        var floor = ConfigurationGeneration.LastChangedUtc;
        var backward = 0;

        Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
        {
            ConfigurationGeneration.Bump();
            if (ConfigurationGeneration.LastChangedUtc < floor)
            {
                System.Threading.Interlocked.Increment(ref backward);
            }
        });

        Assert.Equal(0, backward);
        Assert.True(ConfigurationGeneration.LastChangedUtc >= floor);
        Assert.True(ConfigurationGeneration.LastChangedUtc > DateTime.MinValue);
    }
}
