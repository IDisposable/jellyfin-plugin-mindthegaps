using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The client script matches a row's placement against jellyfin-web's own section type names, so the server
// hands it only a value it can match. The two rows are placed separately, and "none" switches a row off.
public class HomePlacementTests
{
    [Fact]
    public void DiscoverIsOffAndWantToWatchAtTheBottomByDefault()
    {
        Assert.Equal("none", HomePlacement.Discover(new PluginConfiguration()));
        Assert.Equal("bottom", HomePlacement.Wanted(new PluginConfiguration()));
        Assert.Equal("none", HomePlacement.Discover(null));
        Assert.Equal("bottom", HomePlacement.Wanted(null));
    }

    [Theory]
    [InlineData("top", "top")]
    [InlineData("TOP", "top")]
    [InlineData("nextup", "nextup")]
    [InlineData(" Resume ", "resume")]
    [InlineData("LatestMedia", "latestmedia")]
    [InlineData("none", "none")]
    [InlineData("Bottom", "bottom")]
    public void ReadsAKnownPlacementInJellyfinWebsSpelling(string configured, string expected)
    {
        Assert.Equal(expected, HomePlacement.Discover(new PluginConfiguration { HomeDiscoverPlacement = configured }));
        Assert.Equal(expected, HomePlacement.Wanted(new PluginConfiguration { HomeWantedPlacement = configured }));
    }

    [Fact]
    public void PlacesEachRowOnItsOwn()
    {
        var config = new PluginConfiguration { HomeDiscoverPlacement = "top", HomeWantedPlacement = "nextup" };

        Assert.Equal("top", HomePlacement.Discover(config));
        Assert.Equal("nextup", HomePlacement.Wanted(config));
    }

    // Saved before the rows were placed separately: both followed the one shared placement, and Discover
    // had its own switch.
    [Theory]
    [InlineData("", true, "bottom", "bottom")]
    [InlineData("nextup", true, "nextup", "nextup")]
    [InlineData("nextup", false, "none", "nextup")]
    [InlineData("folders", true, "bottom", "bottom")]
    [InlineData("none", true, "bottom", "bottom")]
    public void AnUpgradedConfigurationKeepsBothRowsWhereTheyWere(string shared, bool discoverOn, string discover, string wanted)
    {
        var config = new PluginConfiguration { HomeRowPlacement = shared, HomeRowEnabled = discoverOn };

        Assert.Equal(discover, HomePlacement.Discover(config));
        Assert.Equal(wanted, HomePlacement.Wanted(config));
    }

    [Theory]
    [InlineData("bottom", false)]
    [InlineData("top", false)]
    [InlineData("none", false)]
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

    // An option the server would not recognize would look chosen in the form and do something else.
    [Theory]
    [InlineData("HomeDiscoverPlacement")]
    [InlineData("HomeWantedPlacement")]
    public void TheSettingsFormOffersExactlyThePlacementsTheServerReads(string selectId)
    {
        var assembly = typeof(MindTheGaps.Plugin).Assembly;
        using var stream = assembly.GetManifestResourceStream("Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.settings.html");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var html = reader.ReadToEnd();

        var select = Regex.Match(html, "<select id=\"" + selectId + "\".*?</select>", RegexOptions.Singleline);
        Assert.True(select.Success);
        var offered = Regex.Matches(select.Value, "<option value=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(HomePlacement.Placements.OrderBy(v => v, StringComparer.Ordinal), offered.OrderBy(v => v, StringComparer.Ordinal));
    }
}
