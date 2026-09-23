using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The master switch decides only whether the client script is added to jellyfin-web. Each surface's data is
// governed by its own toggle, so another client can use a surface with no script ever injected.
public class WebUiGateTests
{
    [Fact]
    public void EverythingIsOffByDefault()
    {
        var config = new PluginConfiguration();

        Assert.False(WebUiGate.ScriptInjected(config));
        Assert.False(WebUiGate.PersonPage(config));
        Assert.False(WebUiGate.ItemPage(config));
        Assert.False(WebUiGate.StudioPage(config));
        Assert.False(WebUiGate.HomeRow(config));
    }

    [Fact]
    public void ASurfaceIsServedWithTheMasterSwitchOff()
    {
        var config = new PluginConfiguration
        {
            WebUiEnabled = false,
            PersonPageEnabled = true,
            ItemPageEnabled = true,
            StudioPageEnabled = true,
            HomeRowEnabled = true
        };

        Assert.False(WebUiGate.ScriptInjected(config));
        Assert.True(WebUiGate.PersonPage(config));
        Assert.True(WebUiGate.ItemPage(config));
        Assert.True(WebUiGate.StudioPage(config));
        Assert.True(WebUiGate.HomeRow(config));
    }

    [Fact]
    public void TheMasterSwitchAloneServesNoSurface()
    {
        var config = new PluginConfiguration { WebUiEnabled = true };

        Assert.True(WebUiGate.ScriptInjected(config));
        Assert.False(WebUiGate.PersonPage(config));
        Assert.False(WebUiGate.ItemPage(config));
        Assert.False(WebUiGate.StudioPage(config));
        Assert.False(WebUiGate.HomeRow(config));
    }

    [Fact]
    public void EachSurfaceIsGovernedByItsOwnToggleOnly()
    {
        Assert.True(WebUiGate.PersonPage(new PluginConfiguration { PersonPageEnabled = true }));
        Assert.False(WebUiGate.ItemPage(new PluginConfiguration { PersonPageEnabled = true }));
        Assert.False(WebUiGate.HomeRow(new PluginConfiguration { PersonPageEnabled = true }));

        Assert.True(WebUiGate.ItemPage(new PluginConfiguration { ItemPageEnabled = true }));
        Assert.False(WebUiGate.PersonPage(new PluginConfiguration { ItemPageEnabled = true }));

        Assert.True(WebUiGate.StudioPage(new PluginConfiguration { StudioPageEnabled = true }));
        Assert.False(WebUiGate.ItemPage(new PluginConfiguration { StudioPageEnabled = true }));

        Assert.True(WebUiGate.HomeRow(new PluginConfiguration { HomeRowEnabled = true }));
        Assert.False(WebUiGate.ItemPage(new PluginConfiguration { HomeRowEnabled = true }));
    }

    [Fact]
    public void BeforeThePluginIsInitialized_NothingIsOn()
    {
        Assert.False(WebUiGate.ScriptInjected(null));
        Assert.False(WebUiGate.PersonPage(null));
        Assert.False(WebUiGate.ItemPage(null));
        Assert.False(WebUiGate.StudioPage(null));
        Assert.False(WebUiGate.HomeRow(null));
    }

    [Fact]
    public void WantToWatchIsGovernedByItsOwnToggleOnly()
    {
        Assert.True(WebUiGate.WantToWatch(new PluginConfiguration { WantToWatchEnabled = true }));
        Assert.False(WebUiGate.WantToWatch(new PluginConfiguration { WebUiEnabled = true, PersonPageEnabled = true, ItemPageEnabled = true, HomeRowEnabled = true }));
        Assert.False(WebUiGate.HomeRow(new PluginConfiguration { WantToWatchEnabled = true }));
        Assert.False(WebUiGate.WantToWatch(null));
    }
}
