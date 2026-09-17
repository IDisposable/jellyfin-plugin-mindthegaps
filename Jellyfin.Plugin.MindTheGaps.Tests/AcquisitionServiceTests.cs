using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Api;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
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
        var logger = NullLogger<AcquisitionService>.Instance;

        Assert.Equal(string.Empty, AcquisitionResult.Summarize(null, logger));
        Assert.Equal(string.Empty, AcquisitionResult.Summarize("   ", logger));
        Assert.Equal("one two three", AcquisitionResult.Summarize("one\r\ntwo\nthree", logger));

        var huge = new string('x', 500);
        var summary = AcquisitionResult.Summarize(huge, logger);
        Assert.Equal(203, summary.Length);
        Assert.EndsWith("...", summary, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationMessages_ParsesArrErrorBodies_AndDeduplicates()
    {
        var logger = NullLogger<AcquisitionService>.Instance;
        var body = "[{\"errorMessage\":\"Already added\"},{\"errorMessage\":\"Already added\"},{\"errorMessage\":\"Title not found\"}]";

        var messages = AcquisitionResult.ValidationMessages(body, logger);

        Assert.Equal(new[] { "Already added", "Title not found" }, messages);
        Assert.Equal("Already added Title not found", AcquisitionResult.Summarize(body, logger));
    }

    [Fact]
    public void ResolveApiKey_UsesDefault_WhenPluginIsUninitialized()
    {
        Assert.Equal(TmdbClient.DefaultApiKey, TmdbClient.ResolveApiKey());
    }

    [Fact]
    public async Task SendManyAsync_ContinuesPastFailures_AndAggregatesResults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mtg-acq-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GapStore(NullLogger<GapStore>.Instance, dir);
            store.Save(new GapReport { GeneratedUtc = DateTime.UtcNow, TotalGaps = 2, Items = [new GapItem { Id = "a", Name = "A" }, new GapItem { Id = "b", Name = "B" }] });
            InstallPluginConfiguration(new PluginConfiguration());

            var controller = new AcquisitionController(store, null!);
            var method = typeof(AcquisitionController).GetMethod("SendManyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

            var result = await ((Task<AcquisitionSendResult>)method.Invoke(controller, [
                new[] { "a", "b" },
                (Func<GapItem, PluginConfiguration, CancellationToken, Task<AcquisitionResult>>)((gap, _, _) => Task.FromResult(
                    gap.Id == "a" ? AcquisitionResult.Ok("ok") : AcquisitionResult.Fail("nope"))),
                CancellationToken.None,
            ])!);

            Assert.False(result.Success);
            Assert.Equal(1, result.Succeeded);
            Assert.Equal(1, result.Failed);
            Assert.Equal("Sent 1, 1 failed. First failure: nope", result.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
            SetPluginInstance(null);
        }
    }

    [Fact]
    public async Task SendToArrAsync_WhenMovieHasNoTmdbId_ReturnsFailure()
    {
        var service = new AcquisitionService(null!, null!, new TmdbClient(new MemoryCache(new MemoryCacheOptions())), new MemoryCache(new MemoryCacheOptions()), NullLogger<AcquisitionService>.Instance);
        var gap = new GapItem { Name = "No Tmdb", TargetKind = BaseItemKind.Movie };

        var result = await service.SendToArrAsync(gap, new PluginConfiguration { RadarrUrl = "http://localhost:7878", RadarrApiKey = "k", RadarrQualityProfileId = 1, RadarrRootFolderPath = "/movies" }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("This movie has no TMDB id to send to Radarr.", result.Message);
    }

    [Fact]
    public async Task SendToArrAsync_WhenSeriesHasNoTvdbId_ReturnsFailure()
    {
        var service = new AcquisitionService(null!, null!, new TmdbClient(new MemoryCache(new MemoryCacheOptions())), new MemoryCache(new MemoryCacheOptions()), NullLogger<AcquisitionService>.Instance);
        var gap = new GapItem { Name = "Series", TargetKind = BaseItemKind.Series, SourceItemName = "Series" };

        var result = await service.SendToArrAsync(gap, new PluginConfiguration { SonarrUrl = "http://localhost:8989", SonarrApiKey = "k", SonarrQualityProfileId = 1, SonarrRootFolderPath = "/tv" }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("This series has no TheTVDB id, which Sonarr needs.", result.Message);
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

    private static void InstallPluginConfiguration(PluginConfiguration config)
    {
        var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        SetPluginInstance(plugin);
        typeof(BasePlugin<PluginConfiguration>).GetProperty("Configuration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(plugin, config);
    }

    private static void SetPluginInstance(Plugin? plugin)
    {
        typeof(Plugin).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, plugin);
    }
}
