using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class AcquisitionServiceTests
{
    [Fact]
    public void RadarrConfigured_RequiresUrlKeyProfileAndRoot()
    {
        Assert.False(AcquisitionService.RadarrConfigured(new PluginConfiguration()));

        // URL and key alone are not enough: a movie cannot be added without a quality profile and root folder.
        var partial = new PluginConfiguration { RadarrUrl = "http://localhost:7878", RadarrApiKey = "k" };
        Assert.False(AcquisitionService.RadarrConfigured(partial));

        var full = new PluginConfiguration
        {
            RadarrUrl = "http://localhost:7878",
            RadarrApiKey = "k",
            RadarrQualityProfileId = 1,
            RadarrRootFolderPath = "/movies"
        };
        Assert.True(AcquisitionService.RadarrConfigured(full));
    }

    [Fact]
    public void SonarrConfigured_RequiresUrlKeyProfileAndRoot()
    {
        Assert.False(AcquisitionService.SonarrConfigured(new PluginConfiguration()));

        var partial = new PluginConfiguration { SonarrUrl = "http://localhost:8989", SonarrApiKey = "k", SonarrQualityProfileId = 1 };
        Assert.False(AcquisitionService.SonarrConfigured(partial));

        var full = new PluginConfiguration
        {
            SonarrUrl = "http://localhost:8989",
            SonarrApiKey = "k",
            SonarrQualityProfileId = 1,
            SonarrRootFolderPath = "/tv"
        };
        Assert.True(AcquisitionService.SonarrConfigured(full));
    }

    [Fact]
    public void SeerrConfigured_RequiresUrlAndKey()
    {
        Assert.False(AcquisitionService.SeerrConfigured(new PluginConfiguration()));
        Assert.False(AcquisitionService.SeerrConfigured(new PluginConfiguration { SeerrUrl = "http://localhost:5055" }));

        // Whitespace is not a configured value.
        Assert.False(AcquisitionService.SeerrConfigured(new PluginConfiguration { SeerrUrl = "   ", SeerrApiKey = "k" }));

        Assert.True(AcquisitionService.SeerrConfigured(new PluginConfiguration { SeerrUrl = "http://localhost:5055", SeerrApiKey = "k" }));
    }

    [Fact]
    public void Summarize_CollapsesAndCaps()
    {
        Assert.Equal(string.Empty, AcquisitionResult.Summarize(null));
        Assert.Equal(string.Empty, AcquisitionResult.Summarize("   "));
        Assert.Equal("one two three", AcquisitionResult.Summarize("one\r\ntwo\nthree"));

        var huge = new string('x', 500);
        var summary = AcquisitionResult.Summarize(huge);
        Assert.Equal(203, summary.Length);
        Assert.EndsWith("...", summary, System.StringComparison.Ordinal);
    }

    [Fact]
    public void SeriesTitle_IsTheSeriesForAWholeSeriesGap_AndTheOwningSeriesForAnEpisodeGap()
    {
        // A filmography (or recommendation, or favorites) gap is the series itself; its source is the person
        // or the recommending title, whose name Sonarr must not be given.
        var whole = new GapItem { TargetKind = BaseItemKind.Series, Name = "House", SourceItemName = "Bryan Cranston" };
        Assert.Equal("House", AcquisitionService.SeriesTitle(whole));

        // An episode gap belongs to the owned series named in its source.
        var episode = new GapItem { TargetKind = BaseItemKind.Episode, Name = "S02E05 Breakage", SourceItemName = "Breaking Bad" };
        Assert.Equal("Breaking Bad", AcquisitionService.SeriesTitle(episode));

        var orphan = new GapItem { TargetKind = BaseItemKind.Episode, Name = "S01E01", SourceItemName = null };
        Assert.Equal("S01E01", AcquisitionService.SeriesTitle(orphan));
    }
}
