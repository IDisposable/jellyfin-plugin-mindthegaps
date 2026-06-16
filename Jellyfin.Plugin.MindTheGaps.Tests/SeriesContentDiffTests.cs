using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class SeriesContentDiffTests
{
    private static CanonicalEpisode Ep(int season, int number)
        => new(season, number, $"E{number}", null, null);

    [Fact]
    public void Missing_ReturnsOnlyUnownedEpisodes()
    {
        var canonical = new[] { Ep(1, 1), Ep(1, 2), Ep(1, 3) };
        var owned = new HashSet<(int Season, int Number)> { (1, 1), (1, 3) };

        var missing = SeriesContentDiff.Missing(canonical, owned, 100);

        var episode = Assert.Single(missing);
        Assert.Equal((1, 2), (episode.Season, episode.Number));
    }

    [Fact]
    public void Missing_SkipsSpecialsAndUnnumbered()
    {
        var canonical = new[] { Ep(0, 1), Ep(1, 0), Ep(1, 1) };
        var owned = new HashSet<(int Season, int Number)>();

        var missing = SeriesContentDiff.Missing(canonical, owned, 100);

        var episode = Assert.Single(missing);
        Assert.Equal((1, 1), (episode.Season, episode.Number));
    }

    [Fact]
    public void Missing_DeDupesRepeatedNumbers()
    {
        var canonical = new[] { Ep(1, 1), Ep(1, 1) };
        var owned = new HashSet<(int Season, int Number)>();

        Assert.Single(SeriesContentDiff.Missing(canonical, owned, 100));
    }

    [Fact]
    public void Missing_RespectsCap()
    {
        var canonical = new[] { Ep(1, 1), Ep(1, 2), Ep(1, 3), Ep(1, 4) };
        var owned = new HashSet<(int Season, int Number)>();

        Assert.Equal(2, SeriesContentDiff.Missing(canonical, owned, 2).Count);
    }

    [Fact]
    public void Missing_AllOwned_ReturnsEmpty()
    {
        var canonical = new[] { Ep(1, 1), Ep(1, 2) };
        var owned = new HashSet<(int Season, int Number)> { (1, 1), (1, 2) };

        Assert.Empty(SeriesContentDiff.Missing(canonical, owned, 100));
    }
}
