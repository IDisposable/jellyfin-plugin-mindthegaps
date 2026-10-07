using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The client script places the row beside a section it knows by name, so the server hands it only a placement
// it can act on, and anything else as the spot the row has always had.
public class ItemPlacementTests
{
    [Fact]
    public void DefaultsToAfterMoreLikeThis()
    {
        Assert.Equal("after:similar", ItemPlacement.Of(new PluginConfiguration()));
        Assert.Equal("after:similar", ItemPlacement.Of(null));
    }

    [Theory]
    [InlineData("before:similar", "before:similar")]
    [InlineData(" After:Cast ", "after:cast")]
    [InlineData("BEFORE:CHILDREN", "before:children")]
    public void ReadsAKnownPlacement(string configured, string expected)
    {
        Assert.Equal(expected, ItemPlacement.Of(new PluginConfiguration { ItemPagePlacement = configured }));
    }

    [Theory]
    [InlineData("similar")]
    [InlineData("after:constructor")]
    [InlineData("beside:similar")]
    public void FoldsAnUnknownPlacementToTheDefault(string configured)
    {
        Assert.Equal(ItemPlacement.Default, ItemPlacement.Of(new PluginConfiguration { ItemPagePlacement = configured }));
    }

    // An option the server would fold to the default would look chosen in the form and do nothing.
    [Fact]
    public void TheSettingsFormOffersExactlyThePlacementsTheServerReads()
    {
        var assembly = typeof(MindTheGaps.Plugin).Assembly;
        using var stream = assembly.GetManifestResourceStream("Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.settings.html");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var html = reader.ReadToEnd();

        var select = Regex.Match(html, "<select id=\"ItemPagePlacement\".*?</select>", RegexOptions.Singleline);
        Assert.True(select.Success);
        var offered = Regex.Matches(select.Value, "<option value=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(ItemPlacement.Placements, offered);
    }

    // The script names its sections in mindthegaps.webui.js's ITEM_SECTIONS; a placement naming one it does
    // not know would fall back to the default without a word.
    [Fact]
    public void TheScriptKnowsEverySectionAPlacementNames()
    {
        var assembly = typeof(MindTheGaps.Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("mindthegaps.webui.js", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name);
        using var reader = new StreamReader(stream!);
        var table = Regex.Match(reader.ReadToEnd(), "var ITEM_SECTIONS = \\{([^}]*)\\}");
        Assert.True(table.Success);

        foreach (var section in ItemPlacement.Placements.Select(p => p.Split(':')[1]).Distinct())
        {
            Assert.Contains(section + ":", table.Groups[1].Value, StringComparison.Ordinal);
        }
    }
}
