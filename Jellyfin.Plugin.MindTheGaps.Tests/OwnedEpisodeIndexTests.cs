using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class OwnedEpisodeIndexTests
{
    private static readonly Guid SeriesA = Guid.NewGuid();
    private static readonly Guid SeriesB = Guid.NewGuid();

    private static Episode Ep(Guid series, int? season, int? number, int? numberEnd = null) => new()
    {
        SeriesId = series,
        ParentIndexNumber = season,
        IndexNumber = number,
        IndexNumberEnd = numberEnd
    };

    private static IReadOnlySet<Guid> Wanted(params Guid[] ids) => ids.ToHashSet();

    [Fact]
    public void GroupsEpisodesBySeries()
    {
        var index = OwnedEpisodeIndex.BySeries(
            new BaseItem[] { Ep(SeriesA, 1, 1), Ep(SeriesA, 1, 2), Ep(SeriesB, 2, 5) },
            Wanted(SeriesA, SeriesB));

        Assert.Equal(new HashSet<(int, int)> { (1, 1), (1, 2) }, index[SeriesA]);
        Assert.Equal(new HashSet<(int, int)> { (2, 5) }, index[SeriesB]);
    }

    [Fact]
    public void AMultiEpisodeFileOwnsEveryNumberInItsSpan()
    {
        var index = OwnedEpisodeIndex.BySeries(new BaseItem[] { Ep(SeriesA, 1, 1, 3) }, Wanted(SeriesA));

        Assert.Equal(new HashSet<(int, int)> { (1, 1), (1, 2), (1, 3) }, index[SeriesA]);
    }

    [Fact]
    public void ASpanEndAtOrBelowTheStartOwnsOnlyTheStart()
    {
        var index = OwnedEpisodeIndex.BySeries(new BaseItem[] { Ep(SeriesA, 1, 4, 4), Ep(SeriesA, 1, 6, 5) }, Wanted(SeriesA));

        Assert.Equal(new HashSet<(int, int)> { (1, 4), (1, 6) }, index[SeriesA]);
    }

    [Fact]
    public void SkipsASeriesNobodyAskedAbout()
    {
        var index = OwnedEpisodeIndex.BySeries(new BaseItem[] { Ep(SeriesA, 1, 1), Ep(SeriesB, 1, 1) }, Wanted(SeriesA));

        Assert.True(index.ContainsKey(SeriesA));
        Assert.False(index.ContainsKey(SeriesB));
    }

    [Fact]
    public void SkipsAnEpisodeWithoutASeasonOrANumber_AndAnythingThatIsNotAnEpisode()
    {
        var index = OwnedEpisodeIndex.BySeries(
            new BaseItem[] { Ep(SeriesA, null, 1), Ep(SeriesA, 1, null), new Movie() },
            Wanted(SeriesA));

        Assert.Empty(index);
    }

    [Fact]
    public void ASeriesWithNoOwnedEpisodesIsAbsent()
    {
        Assert.Empty(OwnedEpisodeIndex.BySeries(Array.Empty<BaseItem>(), Wanted(SeriesA)));
    }

    [Fact]
    public void RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => OwnedEpisodeIndex.BySeries(null!, Wanted(SeriesA)));
        Assert.Throws<ArgumentNullException>(() => OwnedEpisodeIndex.BySeries(Array.Empty<BaseItem>(), null!));
    }
}
