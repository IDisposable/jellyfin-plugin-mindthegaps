using Jellyfin.Plugin.MindTheGaps.Configuration;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ConfigIdsTests
{
    [Fact]
    public void ParseInts_ParsesPositiveIdsDeduplicatedInOrder()
    {
        Assert.Equal(new[] { 1, 2, 3 }, ConfigIds.ParseInts("1, 2, 2, 3, 1"));
    }

    [Fact]
    public void ParseInts_DropsBlanksZeroNegativeAndNonNumbers()
    {
        Assert.Equal(new[] { 7, 4 }, ConfigIds.ParseInts("7, , x, -5, 0, 4"));
    }

    [Fact]
    public void ParseInts_NullOrBlankIsEmpty()
    {
        Assert.Empty(ConfigIds.ParseInts(null));
        Assert.Empty(ConfigIds.ParseInts("  "));
    }

    [Fact]
    public void ParseLongs_ParsesLargePositiveIdsDeduplicated()
    {
        Assert.Equal(new[] { 9000000000L, 2L }, ConfigIds.ParseLongs("9000000000, 2, 2"));
    }
}
