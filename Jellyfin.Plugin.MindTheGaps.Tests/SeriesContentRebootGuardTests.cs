using System;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The library source hides missing episodes that fall outside the series' episode era, so a same-named reboot
// the series is mis-tagged as (V 1984 carrying V 2009's ids) is not reported as missing, while the earlier or
// later seasons of a long-running show you only partly own stay listed. EpisodeEra.Expand builds that era (the
// owned run expanded through the contiguous missing episodes) and EpisodeEra.IsOutside is the row filter.
public class SeriesContentRebootGuardTests
{
    [Fact]
    public void PartlyOwnedLongRun_BridgesEarlierSeasonsIntoTheEra()
    {
        // Own 2008-2026 of a show that began in 1974 (NOVA). Its 1974-2007 seasons are missing episodes
        // contiguous with the owned run, so the era spans the whole run and none of them reads as a reboot.
        var missing = Enumerable.Range(1974, 34).ToArray(); // 1974..2007
        Assert.Equal((1974, 2026), EpisodeEra.Expand((2008, 2026), missing));
    }

    [Fact]
    public void Reboot_FarAfterOwnedRun_StaysOutsideTheEra()
    {
        // Own V (1984-1985); the 2009 reboot's episodes are far past the owned run with nothing bridging.
        var era = EpisodeEra.Expand((1984, 1985), new[] { 2009, 2010, 2011 });
        Assert.Equal((1984, 1985), era);
        Assert.True(EpisodeEra.IsOutside(2009, era));
    }

    [Fact]
    public void LateSeason_WithinTheGap_BridgesIntoTheEra()
    {
        // A six-year hiatus is a legitimate late season, not a reboot, so the era stretches to include it.
        var era = EpisodeEra.Expand((2015, 2017), new[] { 2023 });
        Assert.Equal((2015, 2023), era);
        Assert.False(EpisodeEra.IsOutside(2023, era));
    }

    [Fact]
    public void MissingSeasons_ChainThroughTheGap()
    {
        // 2022 bridges from the owned run, then 2024 bridges from 2022, so both stay in one era even though
        // 2024 on its own is more than a reboot-sized gap from the owned run.
        Assert.Equal((2015, 2024), EpisodeEra.Expand((2015, 2017), new[] { 2024, 2022 }));
    }

    [Fact]
    public void Reboot_BeyondTheGap_DoesNotBridge()
    {
        var era = EpisodeEra.Expand((2015, 2017), new[] { 2027 });
        Assert.Equal((2015, 2017), era);
        Assert.True(EpisodeEra.IsOutside(2027, era));
    }

    [Theory]
    [InlineData(2025, 2025)] // exactly max + 8: bridges in (the gap must be exceeded, not met)
    [InlineData(2026, 2017)] // max + 9: does not bridge, the era stops at the owned run
    public void Boundary_BridgesUpToTheGap(int missingYear, int expectedMax)
    {
        Assert.Equal((2015, expectedMax), EpisodeEra.Expand((2015, 2017), new[] { missingYear }));
    }

    [Fact]
    public void NoMissingEpisodes_EraIsTheOwnedRun()
    {
        Assert.Equal((1984, 1985), EpisodeEra.Expand((1984, 1985), null));
        Assert.Equal((1984, 1985), EpisodeEra.Expand((1984, 1985), []));
    }

    [Fact]
    public void RebootBeforeTheEra_IsOutside()
    {
        // Own the 2009 run; a stray 1984 episode is the older same-named series.
        Assert.True(EpisodeEra.IsOutside(1984, (2009, 2011)));
    }

    [Fact]
    public void WithinTheEra_IsInside()
    {
        Assert.False(EpisodeEra.IsOutside(1985, (1984, 1985)));
        Assert.False(EpisodeEra.IsOutside(1974, (1974, 2026)));
    }

    [Fact]
    public void NoEra_CannotJudge_IsInside()
    {
        Assert.False(EpisodeEra.IsOutside(2009, null));
    }

    [Fact]
    public void UndatedEpisode_CannotJudge_IsInside()
    {
        Assert.False(EpisodeEra.IsOutside(null, (1984, 1985)));
    }
}
