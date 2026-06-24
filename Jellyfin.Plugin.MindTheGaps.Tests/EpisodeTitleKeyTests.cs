using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class EpisodeTitleKeyTests
{
    [Theory]
    [InlineData("The Finale (2)")]
    [InlineData("The Finale, Part 2")]
    [InlineData("The Finale Part Two")]
    [InlineData("The Finale Pt. II")]
    [InlineData("The Finale Part II")]
    public void Of_FoldsATrailingPartMarkerToTheBaseTitle(string parted)
        => Assert.Equal(EpisodeTitleKey.Of("The Finale"), EpisodeTitleKey.Of(parted));

    [Fact]
    public void Of_KeepsAYearInParensDistinct()
    {
        // A four-digit year is not a part number, so it is not stripped.
        Assert.NotEqual(EpisodeTitleKey.Of("Pilot"), EpisodeTitleKey.Of("Pilot (2008)"));
    }

    [Fact]
    public void Of_FoldsCaseAndPunctuation()
    {
        Assert.Equal(EpisodeTitleKey.Of("The Finale"), EpisodeTitleKey.Of("the  FINALE!"));
    }

    [Fact]
    public void Of_BlankIsEmpty()
    {
        Assert.Equal(string.Empty, EpisodeTitleKey.Of(null));
        Assert.Equal(string.Empty, EpisodeTitleKey.Of("   "));
    }
}
