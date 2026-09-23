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

    [Fact]
    public void ParseInt_ParsesAnAlreadySplitToken()
    {
        // The widening half of a chip-picker descriptor (ExploreDescriptor.Run/Resolve take a string id;
        // most kinds are plain numeric ids and parse their own back here), not the comma-split parsing
        // ParseInts does for a whole config field.
        Assert.Equal(8267559, ConfigIds.ParseInt("8267559"));
    }

    [Fact]
    public void ParseLong_ParsesAnAlreadySplitToken()
    {
        Assert.Equal(9000000000L, ConfigIds.ParseLong("9000000000"));
    }

    [Fact]
    public void ParseTokens_KeepsIdsAndSlugsTrimmedDeduplicatedInOrder()
    {
        Assert.Equal(new[] { "11416887", "trending", "my-list" }, ConfigIds.ParseTokens(" 11416887 , trending, my-list , trending "));
    }

    [Fact]
    public void ParseTokens_NullOrBlankIsEmpty()
    {
        Assert.Empty(ConfigIds.ParseTokens(null));
        Assert.Empty(ConfigIds.ParseTokens("  "));
    }
}
