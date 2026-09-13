using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class WebUiGapResolverTests
{
    [Theory]
    [InlineData("person", true, GapSource.Person)]
    [InlineData("Item", true, GapSource.Item)]
    [InlineData("HOME", true, GapSource.Home)]
    [InlineData("todo", true, GapSource.Todo)]
    [InlineData("report", false, GapSource.Person)]
    [InlineData("2", false, GapSource.Person)]
    [InlineData("", false, GapSource.Person)]
    [InlineData(null, false, GapSource.Person)]
    public void TryParse_AcceptsTheThreeSurfaceNamesOnly(string? value, bool ok, GapSource expected)
    {
        Assert.Equal(ok, WebUiGapResolver.TryParse(value, out var parsed));
        if (ok)
        {
            Assert.Equal(expected, parsed);
            Assert.Equal(value!.ToLowerInvariant(), WebUiGapResolver.Name(parsed));
        }
    }
}
