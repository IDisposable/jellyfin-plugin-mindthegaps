using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Imdb;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ImdbListInputTests
{
    [Theory]
    [InlineData("ls055576446", "ls055576446")]
    [InlineData("LS055576446", "ls055576446")]
    [InlineData("ur1000000", "ur1000000")]
    [InlineData("https://www.imdb.com/list/ls055576446/", "ls055576446")]
    [InlineData("https://m.imdb.com/list/ls022958322", "ls022958322")]
    [InlineData("imdb.com/user/ur1000000/watchlist/", "ur1000000")]
    [InlineData("https://www.imdb.com/list/ls055576446/?sort=list_order", "ls055576446")]
    public void ParseId_AcceptsBareIdsAndUrls(string token, string expected)
        => Assert.Equal(expected, ImdbListInput.ParseId(token));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ls")]
    [InlineData("12345")]
    [InlineData("controls")]
    // The newer pseudonymous profile address carries no "ls"/"ur" id, and IMDb's API rejects the "p." form,
    // so it is refused at entry rather than failing later on every scan.
    [InlineData("https://www.imdb.com/user/p.eef4dtjnnepbl6zk3gk52tp4pa/watchlist/")]
    public void ParseId_RejectsWhatCannotAddressAList(string token)
        => Assert.Null(ImdbListInput.ParseId(token));

    [Fact]
    public void ParseIds_SplitsTrimsAndDeDuplicates()
    {
        var ids = ImdbListInput.ParseIds(" ls055576446 , https://www.imdb.com/list/ls055576446/ ,ur1000000, ,rubbish");

        Assert.Equal(new[] { "ls055576446", "ur1000000" }, ids);
    }
}
