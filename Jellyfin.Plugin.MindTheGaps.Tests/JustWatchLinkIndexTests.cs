using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class JustWatchLinkIndexTests
{
    private const string MatrixUrl = "https://www.justwatch.com/us/movie/the-matrix";

    private static GapItem Gap(string id, BaseItemKind kind, string tmdbId, params ExternalLink[] links) => new()
    {
        Id = id,
        Name = id,
        TargetKind = kind,
        ProviderIds = new Dictionary<string, string> { ["Tmdb"] = tmdbId },
        Links = links
    };

    private static GapReport Report(params GapItem[] items) => new() { GeneratedUtc = DateTime.UtcNow, TotalGaps = items.Length, Items = items };

    [Fact]
    public void Build_IndexesAMovieAndASeriesByKindAndTmdbId()
    {
        var index = JustWatchLinkIndex.Build(Report(
            Gap("m", BaseItemKind.Movie, "603", new ExternalLink("TMDB", "https://www.themoviedb.org/movie/603"), new ExternalLink("JustWatch", MatrixUrl)),
            Gap("s", BaseItemKind.Series, "1399", new ExternalLink("JustWatch", "https://www.justwatch.com/us/tv-show/game-of-thrones"))));

        Assert.Equal(MatrixUrl, index[(BaseItemKind.Movie, 603)]);
        Assert.Equal("https://www.justwatch.com/us/tv-show/game-of-thrones", index[(BaseItemKind.Series, 1399)]);
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void Build_SkipsGapsThatCannotBeKeyedOrCarryNoJustWatchLink()
    {
        var index = JustWatchLinkIndex.Build(Report(
            Gap("no-link", BaseItemKind.Movie, "1", new ExternalLink("TMDB", "https://www.themoviedb.org/movie/1")),
            Gap("episode", BaseItemKind.Episode, "2", new ExternalLink("JustWatch", MatrixUrl)),
            Gap("bad-id", BaseItemKind.Movie, "abc", new ExternalLink("JustWatch", MatrixUrl)),
            new GapItem { Id = "no-ids", TargetKind = BaseItemKind.Movie, Links = new[] { new ExternalLink("JustWatch", MatrixUrl) } }));

        Assert.Empty(index);
    }

    [Theory]
    [InlineData("https://www.justwatch.com/us/movie/heat", true)]
    [InlineData("https://justwatch.com/us/movie/heat", true)]
    [InlineData("http://www.justwatch.com/us/movie/heat", false)]
    [InlineData("https://evil.example/justwatch.com", false)]
    [InlineData("https://notjustwatch.com/us/movie/heat", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/us/movie/heat", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsJustWatchUrl_AcceptsOnlyHttpsOnTheSitesOwnDomain(string? url, bool expected)
        => Assert.Equal(expected, JustWatchLinkIndex.IsJustWatchUrl(url));

    [Fact]
    public void Build_IgnoresALinkThatIsNotOnJustWatch()
    {
        var index = JustWatchLinkIndex.Build(Report(
            Gap("m", BaseItemKind.Movie, "603", new ExternalLink("JustWatch", "https://evil.example/x"))));

        Assert.Empty(index);
    }

    [Fact]
    public void Find_ReadsTheStore_AndFollowsItsChanges()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mtg-jw-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GapStore(NullLogger<GapStore>.Instance, dir);
            var index = new JustWatchLinkIndex(store);

            Assert.Null(index.Find(BaseItemKind.Movie, 603));

            store.Save(Report(Gap("m", BaseItemKind.Movie, "603", new ExternalLink("JustWatch", MatrixUrl))));

            Assert.Equal(MatrixUrl, index.Find(BaseItemKind.Movie, 603));
            Assert.Null(index.Find(BaseItemKind.Series, 603));
            Assert.Null(index.Find(BaseItemKind.Movie, 604));

            store.Save(Report(Gap("m", BaseItemKind.Movie, "603")));

            Assert.Null(index.Find(BaseItemKind.Movie, 603));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
