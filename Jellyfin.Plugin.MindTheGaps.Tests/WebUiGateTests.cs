using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The master switch decides only whether the client script is added to jellyfin-web. Each surface's data is
// governed by its own placement, "none" being off, so another client can use a surface with no script ever
// injected.
public class WebUiGateTests
{
    private static PluginConfiguration AllShown(bool script) => new()
    {
        WebUiEnabled = script,
        PersonPagePlacement = "after:credits",
        ItemPagePlacement = "after:cast",
        StudioPagePlacement = "after:movies",
        HomeDiscoverPlacement = "top",
        HomeWantedPlacement = "nextup",
        WantToWatchEnabled = true
    };

    [Fact]
    public void EverythingIsOffByDefault()
    {
        var config = new PluginConfiguration();

        Assert.False(WebUiGate.ScriptInjected(config));
        Assert.False(WebUiGate.PersonPage(config));
        Assert.False(WebUiGate.ItemPage(config));
        Assert.False(WebUiGate.StudioPage(config));
        Assert.False(WebUiGate.HomeDiscover(config));
        Assert.False(WebUiGate.HomeWanted(config));
    }

    [Fact]
    public void ASurfaceIsServedWithTheMasterSwitchOff()
    {
        var config = AllShown(script: false);

        Assert.False(WebUiGate.ScriptInjected(config));
        Assert.True(WebUiGate.PersonPage(config));
        Assert.True(WebUiGate.ItemPage(config));
        Assert.True(WebUiGate.StudioPage(config));
        Assert.True(WebUiGate.HomeDiscover(config));
        Assert.True(WebUiGate.HomeWanted(config));
    }

    [Fact]
    public void TheMasterSwitchAloneServesNoSurface()
    {
        var config = new PluginConfiguration { WebUiEnabled = true };

        Assert.True(WebUiGate.ScriptInjected(config));
        Assert.False(WebUiGate.PersonPage(config));
        Assert.False(WebUiGate.ItemPage(config));
        Assert.False(WebUiGate.StudioPage(config));
        Assert.False(WebUiGate.HomeDiscover(config));
    }

    [Fact]
    public void NoneSwitchesEachSurfaceOff()
    {
        var config = new PluginConfiguration
        {
            PersonPagePlacement = "none",
            ItemPagePlacement = "none",
            StudioPagePlacement = "none",
            HomeDiscoverPlacement = "none",
            HomeWantedPlacement = "none",
            WantToWatchEnabled = true,

            // A switch from before the placements does not override a placement that has been chosen.
            PersonPageEnabled = true,
            ItemPageEnabled = true,
            StudioPageEnabled = true,
            HomeRowEnabled = true
        };

        Assert.False(WebUiGate.PersonPage(config));
        Assert.False(WebUiGate.ItemPage(config));
        Assert.False(WebUiGate.StudioPage(config));
        Assert.False(WebUiGate.HomeDiscover(config));
        Assert.False(WebUiGate.HomeWanted(config));
        Assert.True(WebUiGate.WantToWatch(config));
    }

    [Fact]
    public void EachSurfaceIsGovernedByItsOwnPlacementOnly()
    {
        Assert.True(WebUiGate.PersonPage(new PluginConfiguration { PersonPagePlacement = "after:credits" }));
        Assert.False(WebUiGate.ItemPage(new PluginConfiguration { PersonPagePlacement = "after:credits" }));

        Assert.True(WebUiGate.ItemPage(new PluginConfiguration { ItemPagePlacement = "before:similar" }));
        Assert.False(WebUiGate.PersonPage(new PluginConfiguration { ItemPagePlacement = "before:similar" }));

        Assert.True(WebUiGate.StudioPage(new PluginConfiguration { StudioPagePlacement = "after:movies" }));
        Assert.False(WebUiGate.ItemPage(new PluginConfiguration { StudioPagePlacement = "after:movies" }));

        Assert.True(WebUiGate.HomeDiscover(new PluginConfiguration { HomeDiscoverPlacement = "bottom" }));
        Assert.False(WebUiGate.ItemPage(new PluginConfiguration { HomeDiscoverPlacement = "bottom" }));
    }

    // A configuration saved before the placements existed keeps its surfaces as they were.
    [Fact]
    public void AnUpgradedConfigurationKeepsWhatItsSwitchesSaid()
    {
        var on = new PluginConfiguration { PersonPageEnabled = true, ItemPageEnabled = true, StudioPageEnabled = true, HomeRowEnabled = true };
        Assert.True(WebUiGate.PersonPage(on));
        Assert.True(WebUiGate.ItemPage(on));
        Assert.True(WebUiGate.StudioPage(on));
        Assert.True(WebUiGate.HomeDiscover(on));

        var off = new PluginConfiguration();
        Assert.False(WebUiGate.PersonPage(off));
        Assert.False(WebUiGate.ItemPage(off));
        Assert.False(WebUiGate.StudioPage(off));
        Assert.False(WebUiGate.HomeDiscover(off));
    }

    [Fact]
    public void NotInterestedIsItsOwnToggle_OffByDefault()
    {
        Assert.False(WebUiGate.NotInterested(null));
        Assert.False(WebUiGate.NotInterested(new PluginConfiguration()));
        Assert.False(WebUiGate.NotInterested(new PluginConfiguration { WantToWatchEnabled = true }));
        Assert.True(WebUiGate.NotInterested(new PluginConfiguration { NotInterestedEnabled = true }));
    }

    // The Want to watch row was shown wherever want to watch was on, so an upgraded configuration keeps it.
    [Fact]
    public void TheWantToWatchRowNeedsWantToWatchAndAPlacement()
    {
        Assert.True(WebUiGate.HomeWanted(new PluginConfiguration { WantToWatchEnabled = true }));
        Assert.False(WebUiGate.HomeWanted(new PluginConfiguration { HomeWantedPlacement = "top" }));
        Assert.False(WebUiGate.HomeWanted(new PluginConfiguration { WantToWatchEnabled = true, HomeWantedPlacement = "none" }));
    }

    [Fact]
    public void BeforeThePluginIsInitialized_NothingIsOn()
    {
        Assert.False(WebUiGate.ScriptInjected(null));
        Assert.False(WebUiGate.PersonPage(null));
        Assert.False(WebUiGate.ItemPage(null));
        Assert.False(WebUiGate.StudioPage(null));
        Assert.False(WebUiGate.HomeDiscover(null));
        Assert.False(WebUiGate.HomeWanted(null));
    }

    [Fact]
    public void WantToWatchIsGovernedByItsOwnToggleOnly()
    {
        Assert.True(WebUiGate.WantToWatch(new PluginConfiguration { WantToWatchEnabled = true }));
        var surfacesOnly = AllShown(script: true);
        surfacesOnly.WantToWatchEnabled = false;
        Assert.False(WebUiGate.WantToWatch(surfacesOnly));
        Assert.False(WebUiGate.HomeDiscover(new PluginConfiguration { WantToWatchEnabled = true }));
        Assert.False(WebUiGate.WantToWatch(null));
    }
}
