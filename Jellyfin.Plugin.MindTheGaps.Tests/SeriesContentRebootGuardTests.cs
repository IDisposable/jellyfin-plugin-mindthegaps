using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Library;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The library source hides missing episodes that fall outside the owned run by a reboot-sized gap, so a
// same-named reboot the series is mis-tagged as (V 1984 carrying V 2009's ids) is not reported as missing.
// IsOutsideOwnedEra is the pure era test those rows are filtered by.
public class SeriesContentRebootGuardTests
{
    [Fact]
    public void Reboot_FarAfterOwnedRun_IsOutsideEra()
    {
        // Own V (1984-1985); a 2009 episode is the reboot, not a real gap.
        Assert.True(SeriesContentGapSource.IsOutsideOwnedEra(2009, (1984, 1985)));
    }

    [Fact]
    public void Reboot_FarBeforeOwnedRun_IsOutsideEra()
    {
        // Own the 2009 run; a 1984 episode is the older same-named series.
        Assert.True(SeriesContentGapSource.IsOutsideOwnedEra(1984, (2009, 2011)));
    }

    [Fact]
    public void SameEra_IsInsideEra()
    {
        Assert.False(SeriesContentGapSource.IsOutsideOwnedEra(1985, (1984, 1985)));
    }

    [Fact]
    public void LateSeason_WithinHiatus_StaysInsideEra()
    {
        // A six-year hiatus is a legitimate late season, not a reboot, so it must still be listed.
        Assert.False(SeriesContentGapSource.IsOutsideOwnedEra(2023, (2015, 2017)));
    }

    [Fact]
    public void LateSeason_BeyondRebootGap_IsOutsideEra()
    {
        // A ten-year gap reads as a reboot under the conservative threshold.
        Assert.True(SeriesContentGapSource.IsOutsideOwnedEra(2027, (2015, 2017)));
    }

    [Theory]
    [InlineData(2025, false)] // exactly max + 8: still inside (the gap must be exceeded, not met)
    [InlineData(2026, true)]  // max + 9: outside
    public void Boundary_IsExclusiveOfTheGap(int year, bool expected)
    {
        Assert.Equal(expected, SeriesContentGapSource.IsOutsideOwnedEra(year, (2015, 2017)));
    }

    [Fact]
    public void NoOwnedRun_CannotJudge_IsInsideEra()
    {
        Assert.False(SeriesContentGapSource.IsOutsideOwnedEra(2009, null));
    }

    [Fact]
    public void UndatedEpisode_CannotJudge_IsInsideEra()
    {
        Assert.False(SeriesContentGapSource.IsOutsideOwnedEra(null, (1984, 1985)));
    }
}
