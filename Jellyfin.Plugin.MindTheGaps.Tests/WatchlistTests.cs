using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class WatchlistTests
{
    [Fact]
    public void ValidationMessages_ReadsArrRejections_AndSummarizeUsesThem()
    {
        const string body = """
            [{"propertyName":"TmdbId","errorMessage":"This movie has already been added","attemptedValue":1421903,"severity":"error","errorCode":"MovieExistsValidator","formattedMessageArguments":[]},
             {"propertyName":"Path","errorMessage":"This movie has already been added","severity":"error"},
             {"propertyName":"QualityProfileId","errorMessage":"Quality profile does not exist","severity":"error"}]
            """;

        Assert.Equal(["This movie has already been added", "Quality profile does not exist"], AcquisitionResult.ValidationMessages(body));
        Assert.Equal("This movie has already been added Quality profile does not exist", AcquisitionResult.Summarize(body));

        Assert.Empty(AcquisitionResult.ValidationMessages("{\"message\":\"not an array\"}"));
        Assert.Empty(AcquisitionResult.ValidationMessages("[not json"));
        Assert.Equal("plain text error", AcquisitionResult.Summarize("plain   text\n error"));
    }

    [Fact]
    public void ParsePresence_ReadsTmdbIdFileAndMonitoredState_ForMoviesAndSeries()
    {
        using var movies = JsonDocument.Parse("""
            [{"tmdbId":275,"hasFile":true,"monitored":true},{"tmdbId":1421903,"hasFile":false,"monitored":true},{"tmdbId":5,"hasFile":false,"monitored":false},{"tmdbId":0},{"title":"no id"}]
            """);
        var p = AcquisitionService.ParsePresence(movies.RootElement);
        Assert.Equal(3, p.Count);
        Assert.True(p[275].HasFile);
        Assert.True(p[1421903].Monitored);
        Assert.False(p[1421903].HasFile);
        Assert.False(p[5].Monitored);

        using var series = JsonDocument.Parse("""
            [{"tmdbId":1408,"monitored":true,"statistics":{"episodeFileCount":12}},{"tmdbId":86430,"monitored":true,"statistics":{"episodeFileCount":0}}]
            """);
        var s = AcquisitionService.ParsePresence(series.RootElement);
        Assert.True(s[1408].HasFile);
        Assert.False(s[86430].HasFile);

        using var notArray = JsonDocument.Parse("{}");
        Assert.Empty(AcquisitionService.ParsePresence(notArray.RootElement));
    }

    [Theory]
    [InlineData(UserDataSaveReason.PlaybackFinished, true, true)]
    [InlineData(UserDataSaveReason.TogglePlayed, true, true)]
    [InlineData(UserDataSaveReason.TogglePlayed, false, false)]
    [InlineData(UserDataSaveReason.PlaybackProgress, true, false)]
    [InlineData(UserDataSaveReason.PlaybackStart, true, false)]
    [InlineData(UserDataSaveReason.PlaybackFinished, false, false)]
    public void IsWatchedSave_OnlyFinishedOrToggledToPlayed(UserDataSaveReason reason, bool played, bool expected)
        => Assert.Equal(expected, WatchedAutoRemover.IsWatchedSave(reason, played));

    [Theory]
    [InlineData("recommendation:movie:1421903", true, BaseItemKind.Movie, 1421903)]
    [InlineData("recommendation:series:1408", true, BaseItemKind.Series, 1408)]
    [InlineData("recommendation:movie:0", false, default(BaseItemKind), 0)]
    [InlineData("recommendation:movie:abc", false, default(BaseItemKind), 0)]
    [InlineData("filmography:movie:13", false, default(BaseItemKind), 0)]
    [InlineData("", false, default(BaseItemKind), 0)]
    [InlineData(null, false, default(BaseItemKind), 0)]
    public void SearchIds_ParseOnlyRecommendationShapedIds(string? id, bool ok, BaseItemKind kind, int tmdbId)
    {
        Assert.Equal(ok, WatchlistSearchService.TryParseId(id, out var parsedKind, out var parsedId));
        if (ok)
        {
            Assert.Equal(kind, parsedKind);
            Assert.Equal(tmdbId, parsedId);
        }
    }

    [Theory]
    [InlineData("Want to watch", true)]
    [InlineData("  want TO watch ", true)]
    [InlineData("Want to watch later", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PlaylistName_MatchesIgnoringCaseAndWhitespace(string? name, bool expected)
        => Assert.Equal(expected, WatchlistPlaylistService.Matches(name));
}
