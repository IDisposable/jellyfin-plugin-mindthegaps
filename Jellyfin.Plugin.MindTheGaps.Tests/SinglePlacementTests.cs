using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The person and studio surfaces each have one place on their page, so their placement is off or shown.
public class SinglePlacementTests
{
    [Theory]
    [InlineData("", false, "none")]
    [InlineData("", true, PersonPlacement.Shown)]
    [InlineData("after:credits", false, PersonPlacement.Shown)]
    [InlineData("NONE", true, "none")]
    [InlineData("before:similar", true, PersonPlacement.Shown)]
    public void ReadsThePersonPlacement_OrTheSwitchItReplaced(string configured, bool enabled, string expected)
        => Assert.Equal(expected, PersonPlacement.Of(new PluginConfiguration { PersonPagePlacement = configured, PersonPageEnabled = enabled }));

    [Theory]
    [InlineData("", false, "none")]
    [InlineData("", true, StudioPlacement.Shown)]
    [InlineData(" After:Movies ", false, StudioPlacement.Shown)]
    [InlineData("none", true, "none")]
    public void ReadsTheStudioPlacement_OrTheSwitchItReplaced(string configured, bool enabled, string expected)
        => Assert.Equal(expected, StudioPlacement.Of(new PluginConfiguration { StudioPagePlacement = configured, StudioPageEnabled = enabled }));

    [Fact]
    public void BothAreOffBeforeThePluginIsInitialized()
    {
        Assert.Equal("none", PersonPlacement.Of(null));
        Assert.Equal("none", StudioPlacement.Of(null));
    }

    public static TheoryData<string, IReadOnlyList<string>> Selects() => new()
    {
        { "PersonPagePlacement", PersonPlacement.Placements },
        { "StudioPagePlacement", StudioPlacement.Placements },
    };

    // An option the server would not recognize would look chosen in the form and do something else.
    [Theory]
    [MemberData(nameof(Selects))]
    public void TheSettingsFormOffersExactlyThePlacementsTheServerReads(string selectId, IReadOnlyList<string> placements)
    {
        var assembly = typeof(MindTheGaps.Plugin).Assembly;
        using var stream = assembly.GetManifestResourceStream("Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.settings.html");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);

        var select = Regex.Match(reader.ReadToEnd(), "<select id=\"" + selectId + "\".*?</select>", RegexOptions.Singleline);
        Assert.True(select.Success);
        var offered = Regex.Matches(select.Value, "<option value=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(placements, offered);
    }
}
