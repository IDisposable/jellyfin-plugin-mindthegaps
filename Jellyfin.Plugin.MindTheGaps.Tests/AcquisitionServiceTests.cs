using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

[Collection("PluginConfiguration")]
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
    public async Task SendManyAsync_WhenNoIdsAreInReport_ReturnsFailureWithoutSending()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mtg-acq-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GapStore(NullLogger<GapStore>.Instance, dir);
            store.Save(new GapReport { GeneratedUtc = DateTime.UtcNow, TotalGaps = 1, Items = [new GapItem { Id = "present", Name = "Present" }] });
            var controller = new AcquisitionController(store, null!);
            var method = typeof(AcquisitionController).GetMethod("SendManyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var sends = 0;

            var result = await ((Task<AcquisitionSendResult>)method.Invoke(controller, [
                new[] { "missing" },
                (Func<GapItem, PluginConfiguration, CancellationToken, Task<AcquisitionResult>>)((_, _, _) =>
                {
                    sends++;
                    return Task.FromResult(AcquisitionResult.Ok());
                }),
                CancellationToken.None,
            ])!);

            Assert.False(result.Success);
            Assert.Equal(0, sends);
            Assert.Equal("None of those gaps are in the current report; rescan and try again.", result.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task SendToArrAsync_WhenMovieHasNoTmdbId_ReturnsFailure()
    {
        var service = new AcquisitionService(null!, null!, new TmdbClient(new MemoryCache(new MemoryCacheOptions())), new MemoryCache(new MemoryCacheOptions()), NullLogger<AcquisitionService>.Instance);
        var gap = new GapItem { Name = "No Tmdb", TargetKind = BaseItemKind.Movie };

        var result = await service.SendToArrAsync(gap, new PluginConfiguration { RadarrUrl = "http://localhost:7878", RadarrApiKey = "k", RadarrQualityProfileId = 1, RadarrRootFolderPath = "/movies" }, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("This movie has no TMDB id to send to Radarr.", result.Message);
    }

    [Fact]
    public async Task SendToArrAsync_WhenSeriesHasNoTvdbId_ReturnsFailure()
    {
        var service = new AcquisitionService(null!, null!, new TmdbClient(new MemoryCache(new MemoryCacheOptions())), new MemoryCache(new MemoryCacheOptions()), NullLogger<AcquisitionService>.Instance);
        var gap = new GapItem { Name = "Series", TargetKind = BaseItemKind.Series, SourceItemName = "Series" };

        var result = await service.SendToArrAsync(gap, new PluginConfiguration { SonarrUrl = "http://localhost:8989", SonarrApiKey = "k", SonarrQualityProfileId = 1, SonarrRootFolderPath = "/tv" }, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("This series has no TheTVDB id, which Sonarr needs.", result.Message);
    }

    [Fact]
    public async Task SendToArrAsync_MoviePostsRadarrPayloadAndApiKey()
    {
        var handler = new AcquisitionHandler(HttpStatusCode.Accepted);
        var service = CreateService(handler);
        var gap = new GapItem
        {
            Name = "The Matrix",
            TargetKind = BaseItemKind.Movie,
            ProviderIds = new Dictionary<string, string> { [ProviderIds.Tmdb] = "603" }
        };

        var result = await service.SendToArrAsync(gap, new PluginConfiguration
        {
            RadarrUrl = "http://radarr.test/",
            RadarrApiKey = "radarr-key",
            RadarrQualityProfileId = 7,
            RadarrRootFolderPath = "/movies"
        }, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("http://radarr.test/api/v3/movie", handler.Request!.RequestUri!.ToString());
        Assert.Equal("radarr-key", handler.Request.Headers.GetValues("X-Api-Key").Single());
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("The Matrix", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(603, body.RootElement.GetProperty("tmdbId").GetInt32());
        Assert.Equal(7, body.RootElement.GetProperty("qualityProfileId").GetInt32());
        Assert.True(body.RootElement.GetProperty("addOptions").GetProperty("searchForMovie").GetBoolean());
    }

    [Fact]
    public async Task SendToArrAsync_QualityProfileIdOverridesTheConfiguredDefault()
    {
        var handler = new AcquisitionHandler(HttpStatusCode.Accepted);
        var service = CreateService(handler);
        var gap = new GapItem
        {
            Name = "The Matrix",
            TargetKind = BaseItemKind.Movie,
            ProviderIds = new Dictionary<string, string> { [ProviderIds.Tmdb] = "603" }
        };

        var result = await service.SendToArrAsync(gap, new PluginConfiguration
        {
            RadarrUrl = "http://radarr.test/",
            RadarrApiKey = "radarr-key",
            RadarrQualityProfileId = 7,
            RadarrRootFolderPath = "/movies"
        }, 99, CancellationToken.None);

        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(99, body.RootElement.GetProperty("qualityProfileId").GetInt32());
    }

    [Fact]
    public async Task GetRadarrQualityProfilesAsync_ReturnsProfilesFromTheApi()
    {
        var handler = new AcquisitionHandler(HttpStatusCode.OK, "[{\"id\":1,\"name\":\"HD-1080p\"},{\"id\":2,\"name\":\"Ultra-HD\"}]");
        var service = CreateService(handler);
        var config = new PluginConfiguration { RadarrUrl = "http://radarr.test", RadarrApiKey = "key", RadarrQualityProfileId = 1, RadarrRootFolderPath = "/movies" };

        var profiles = await service.GetRadarrQualityProfilesAsync(config, CancellationToken.None);

        Assert.Equal(2, profiles.Count);
        Assert.Equal("HD-1080p", profiles[0].Name);
        Assert.Equal(1, profiles[0].Id);
        Assert.Equal("http://radarr.test/api/v3/qualityprofile", handler.Request!.RequestUri!.ToString());
        Assert.Equal("key", handler.Request.Headers.GetValues("X-Api-Key").Single());
        Assert.Equal(HttpMethod.Get, handler.Request.Method);
    }

    [Fact]
    public async Task GetRadarrQualityProfilesAsync_EmptyWhenNotConfigured()
    {
        var handler = new AcquisitionHandler(HttpStatusCode.OK, "[]");
        var service = CreateService(handler);

        var profiles = await service.GetRadarrQualityProfilesAsync(new PluginConfiguration(), CancellationToken.None);

        Assert.Empty(profiles);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task GetSonarrQualityProfilesAsync_ReturnsProfilesFromTheApi()
    {
        var handler = new AcquisitionHandler(HttpStatusCode.OK, "[{\"id\":3,\"name\":\"WEB-1080p\"}]");
        var service = CreateService(handler);
        var config = new PluginConfiguration { SonarrUrl = "http://sonarr.test", SonarrApiKey = "key", SonarrQualityProfileId = 1, SonarrRootFolderPath = "/tv" };

        var profiles = await service.GetSonarrQualityProfilesAsync(config, CancellationToken.None);

        Assert.Single(profiles);
        Assert.Equal("WEB-1080p", profiles[0].Name);
        Assert.Equal("http://sonarr.test/api/v3/qualityprofile", handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetRadarrQualityProfilesAsync_EmptyOnNonSuccessStatus()
    {
        var handler = new AcquisitionHandler(HttpStatusCode.Unauthorized);
        var service = CreateService(handler);
        var config = new PluginConfiguration { RadarrUrl = "http://radarr.test", RadarrApiKey = "bad-key", RadarrQualityProfileId = 1, RadarrRootFolderPath = "/movies" };

        var profiles = await service.GetRadarrQualityProfilesAsync(config, CancellationToken.None);

        Assert.Empty(profiles);
    }

    [Fact]
    public async Task SendToSeerrAsync_SeriesPostsTvRequest()
    {
        var handler = new AcquisitionHandler(HttpStatusCode.OK);
        var service = CreateService(handler);
        var gap = new GapItem
        {
            Name = "The Wire",
            TargetKind = BaseItemKind.Series,
            ProviderIds = new Dictionary<string, string> { [ProviderIds.Tmdb] = "1399" }
        };

        var result = await service.SendToSeerrAsync(gap, new PluginConfiguration
        {
            SeerrUrl = "https://seerr.test",
            SeerrApiKey = "seerr-key"
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://seerr.test/api/v1/request", handler.Request!.RequestUri!.ToString());
        Assert.Equal("seerr-key", handler.Request.Headers.GetValues("X-Api-Key").Single());
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("tv", body.RootElement.GetProperty("mediaType").GetString());
        Assert.Equal(1399, body.RootElement.GetProperty("mediaId").GetInt32());
    }

    [Fact]
    public async Task SendToArrAsync_InvalidUrlReturnsFailureWithoutSending()
    {
        var handler = new AcquisitionHandler(HttpStatusCode.OK);
        var service = CreateService(handler);
        var gap = new GapItem
        {
            Name = "The Matrix",
            TargetKind = BaseItemKind.Movie,
            ProviderIds = new Dictionary<string, string> { [ProviderIds.Tmdb] = "603" }
        };

        var result = await service.SendToArrAsync(gap, new PluginConfiguration
        {
            RadarrUrl = "ftp://radarr.test",
            RadarrApiKey = "key",
            RadarrQualityProfileId = 7,
            RadarrRootFolderPath = "/movies"
        }, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not a valid http(s) address", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SendToArrAsync_NonSuccessReturnsArrValidationMessage()
    {
        var handler = new AcquisitionHandler(HttpStatusCode.BadRequest, "[{\"errorMessage\":\"Already added\"}]");
        var service = CreateService(handler);
        var gap = new GapItem
        {
            Name = "The Matrix",
            TargetKind = BaseItemKind.Movie,
            ProviderIds = new Dictionary<string, string> { [ProviderIds.Tmdb] = "603" }
        };

        var result = await service.SendToArrAsync(gap, new PluginConfiguration
        {
            RadarrUrl = "http://radarr.test",
            RadarrApiKey = "key",
            RadarrQualityProfileId = 7,
            RadarrRootFolderPath = "/movies"
        }, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Radarr returned 400. Already added", result.Message);
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

    private static AcquisitionService CreateService(AcquisitionHandler handler)
        => new(new AcquisitionFactory(handler), null!, new TmdbClient(new MemoryCache(new MemoryCacheOptions())), new MemoryCache(new MemoryCacheOptions()), NullLogger<AcquisitionService>.Instance);

    private sealed class AcquisitionFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public AcquisitionFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class AcquisitionHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public AcquisitionHandler(HttpStatusCode status, string body = "")
        {
            _status = status;
            _body = body;
        }

        public int Calls { get; private set; }

        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body) };
        }
    }
}
