using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class GapScanContextTests
{
    private static GapScanContext ContextOwning(string provider, string id)
    {
        var dict = new Dictionary<string, BaseItem>
        {
            [OwnershipIndex.MakeKey(BaseItemKind.Movie, provider, id)] = null!
        };
        return new GapScanContext(new PluginConfiguration(), new OwnershipIndex(dict));
    }

    [Fact]
    public void IsOwned_TrueWhenAnyProviderIdMatches()
    {
        var context = ContextOwning("Imdb", "tt0133093");
        var candidate = new Dictionary<string, string> { ["Tmdb"] = "603", ["Imdb"] = "tt0133093" };
        Assert.True(context.IsOwned(BaseItemKind.Movie, candidate));
    }

    [Fact]
    public void IsOwned_FalseWhenNoMatch()
    {
        var context = ContextOwning("Tmdb", "999");
        var candidate = new Dictionary<string, string> { ["Tmdb"] = "603" };
        Assert.False(context.IsOwned(BaseItemKind.Movie, candidate));
    }
}
