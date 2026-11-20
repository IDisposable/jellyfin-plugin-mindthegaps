using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class LegacySourceKindsTests
{
    [Fact]
    public void Recover_RetagsAnImdbWatchlistStoredAsAList()
    {
        var gap = new GapItem { Id = "imdblist:ls000023761:tt1", SourceItemType = SourceItemTypes.ImdbList, SourceItemName = "WATCHLIST" };

        LegacySourceKinds.Recover(gap);

        Assert.Equal(SourceItemTypes.ImdbWatchlist, gap.SourceItemType);
    }

    [Theory]
    [InlineData(SourceItemTypes.ImdbList, "L-Movies")]
    [InlineData(SourceItemTypes.TraktList, "WATCHLIST")]
    public void Recover_LeavesEverythingElse(string kind, string name)
    {
        var gap = new GapItem { Id = "x", SourceItemType = kind, SourceItemName = name };

        LegacySourceKinds.Recover(gap);

        Assert.Equal(kind, gap.SourceItemType);
    }
}
