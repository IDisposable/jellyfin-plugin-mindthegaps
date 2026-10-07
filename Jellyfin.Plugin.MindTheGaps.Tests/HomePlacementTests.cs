using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The client script matches the placement against jellyfin-web's own section type names, so the server
// hands it only a value it can match, and anything else as the bottom of the page.
public class HomePlacementTests
{
    [Fact]
    public void DefaultsToTheBottom()
    {
        Assert.Equal(string.Empty, HomePlacement.Of(new PluginConfiguration()));
        Assert.Equal(string.Empty, HomePlacement.Of(null));
    }

    [Theory]
    [InlineData("top", "top")]
    [InlineData("TOP", "top")]
    [InlineData("nextup", "nextup")]
    [InlineData(" Resume ", "resume")]
    [InlineData("LatestMedia", "latestmedia")]
    public void ReadsAKnownPlacementInJellyfinWebsSpelling(string configured, string expected)
    {
        Assert.Equal(expected, HomePlacement.Of(new PluginConfiguration { HomeRowPlacement = configured }));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("folders")]
    [InlineData("somethingelse")]
    public void FoldsAnUnknownPlacementToTheBottom(string configured)
    {
        Assert.Equal(string.Empty, HomePlacement.Of(new PluginConfiguration { HomeRowPlacement = configured }));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("top", false)]
    [InlineData("nextup", true)]
    public void OnlyASectionTypeNeedsTheUsersSlotOrder(string placement, bool needed)
    {
        Assert.Equal(needed, HomePlacement.FollowsSection(placement));
    }

    // The server stores a slot's type as core's HomeSectionType name; jellyfin-web reads a slot never saved as
    // its default for that position.
    [Fact]
    public void ResolvesStoredSlotsInJellyfinWebsSpelling_AndDefaultsTheRest()
    {
        var order = HomePlacement.Resolve(new[] { (0, "NextUp"), (1, "SmallLibraryTiles"), (9, "LatestMedia") });

        Assert.Equal(
            new[] { "nextup", "smalllibrarytiles", "resumeaudio", "resumebook", "livetv", "nextup", "latestmedia", "none", "none", "latestmedia" },
            order);
    }

    [Fact]
    public void ResolvesNothingStoredToJellyfinWebsDefaults()
    {
        Assert.Equal(HomePlacement.DefaultOrder, HomePlacement.Resolve(Array.Empty<(int, string)>()));
    }

    [Fact]
    public void ResolvesAnUnknownTypeAsAnEmptySlot_AndIgnoresASlotOutOfRange()
    {
        var order = HomePlacement.Resolve(new[] { (2, "None"), (3, "Bogus"), (10, "NextUp"), (-1, "Resume") });

        Assert.Equal("none", order[2]);
        Assert.Equal("none", order[3]);
        Assert.Equal(10, order.Count);
        Assert.Equal("smalllibrarytiles", order[0]);
    }

    // An option the server would fold to the bottom would look chosen in the form and do nothing.
    [Fact]
    public void TheSettingsFormOffersExactlyThePlacementsTheServerReads()
    {
        var assembly = typeof(MindTheGaps.Plugin).Assembly;
        using var stream = assembly.GetManifestResourceStream("Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.settings.html");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var html = reader.ReadToEnd();

        var select = Regex.Match(html, "<select id=\"HomeRowPlacement\".*?</select>", RegexOptions.Singleline);
        Assert.True(select.Success);
        var offered = Regex.Matches(select.Value, "<option value=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();

        var expected = new[] { string.Empty, HomePlacement.Top }.Concat(HomePlacement.SectionTypes).ToList();
        Assert.Equal(expected.OrderBy(v => v, StringComparer.Ordinal), offered.OrderBy(v => v, StringComparer.Ordinal));
    }
}
