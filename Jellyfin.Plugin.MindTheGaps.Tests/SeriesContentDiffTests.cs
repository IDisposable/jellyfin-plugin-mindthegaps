using System;
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

    private static CanonicalEpisode EpAired(int season, int number, int year)
        => new(season, number, $"E{number}", new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc), null);

    [Fact]
    public void LooksLikeDifferentSeries_RebootYearFarFromOwned_IsFlagged()
    {
        // Owned "V" is the 1984 series; the resolved show's first season aired 2009 (the reboot).
        var canonical = new[] { EpAired(1, 1, 2009), EpAired(2, 1, 2010), EpAired(3, 1, 2011) };

        Assert.True(SeriesContentGapSourceBase.LooksLikeDifferentSeries(1984, canonical));
    }

    [Fact]
    public void LooksLikeDifferentSeries_SameStartYear_NotFlagged_EvenWithLaterSeasons()
    {
        // A legitimate long run: season one matches the owned start year; later seasons years later do not
        // flag it (the guard compares the lowest season's year, not every episode's).
        var canonical = new[] { EpAired(1, 1, 2000), EpAired(2, 1, 2001), EpAired(10, 1, 2010) };

        Assert.False(SeriesContentGapSourceBase.LooksLikeDifferentSeries(2000, canonical));
    }

    [Fact]
    public void LooksLikeDifferentSeries_NoOwnedYear_NotFlagged()
    {
        Assert.False(SeriesContentGapSourceBase.LooksLikeDifferentSeries(null, new[] { EpAired(1, 1, 2009) }));
    }
}
