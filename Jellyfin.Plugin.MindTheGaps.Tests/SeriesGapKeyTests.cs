using System;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class SeriesGapKeyTests
{
    [Fact]
    public void Episode_IsStableAndZeroPadded()
    {
        var seriesId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.Equal("seriescontent:11111111222233334444555555555555:s02e05", SeriesGapKey.Episode(seriesId, 2, 5));
    }

    [Fact]
    public void Episode_SameInputs_SameKey()
    {
        var seriesId = Guid.NewGuid();

        Assert.Equal(SeriesGapKey.Episode(seriesId, 1, 1), SeriesGapKey.Episode(seriesId, 1, 1));
    }

    [Fact]
    public void Episode_DifferentEpisodes_DifferentKeys()
    {
        var seriesId = Guid.NewGuid();

        Assert.NotEqual(SeriesGapKey.Episode(seriesId, 1, 1), SeriesGapKey.Episode(seriesId, 1, 2));
    }
}
