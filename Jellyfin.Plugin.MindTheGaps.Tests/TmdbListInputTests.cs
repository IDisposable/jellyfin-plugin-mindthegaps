using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class TmdbListInputTests
{
    [Theory]
    [InlineData("12345", 12345)]
    [InlineData("  12345  ", 12345)]
    [InlineData("https://www.themoviedb.org/list/12345", 12345)]
    [InlineData("https://www.themoviedb.org/list/12345-my-favourite-films", 12345)]
    [InlineData("themoviedb.org/list/8175", 8175)]
    [InlineData("https://www.themoviedb.org/list/8175?page=1", 8175)]
    [InlineData("www.themoviedb.org/list/777", 777)]
    public void ParseId_ExtractsTheListId(string token, int expected)
    {
        Assert.Equal(expected, TmdbListInput.ParseId(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-list")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("https://www.themoviedb.org/movie/550")]
    [InlineData("https://www.themoviedb.org/list/")]
    [InlineData("https://www.themoviedb.org/list/abc")]
    public void ParseId_ReturnsNull_ForNonListTokens(string? token)
    {
        Assert.Null(TmdbListInput.ParseId(token));
    }

    [Fact]
    public void ParseIds_ParsesMixedTokens_DeDupedInOrder()
    {
        var ids = TmdbListInput.ParseIds("12345, https://www.themoviedb.org/list/678-name, 12345, , not-a-list, 90");

        Assert.Equal(3, ids.Count);
        Assert.Equal(12345, ids[0]);
        Assert.Equal(678, ids[1]);
        Assert.Equal(90, ids[2]);
    }

    [Fact]
    public void ParseIds_ReturnsEmpty_ForBlank()
    {
        Assert.Empty(TmdbListInput.ParseIds(null));
        Assert.Empty(TmdbListInput.ParseIds("   "));
    }
}
