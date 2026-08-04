using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Trakt;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Trakt;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Real response captured from the Trakt list items endpoint. Trakt needs a private client id, so unlike the
// keyless sources it must be captured with one; the response carries no client id, so it stays safe to commit.
// Re-capture with (the list mixes movies and shows, exercising both the Movies and Shows routing):
//   curl -s -H "trakt-api-key: <YOUR_CLIENT_ID>" -H "trakt-api-version: 2" \
//     "https://api.trakt.tv/lists/11416887/items/movie,show?limit=12&extended=full" -o trakt_list_items.json
public class TraktListTests
{
    private const string ListId = "11416887";
    private const string ListName = "True Crime Documentary Series & Film";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private static IReadOnlyList<TraktListItem> LoadItems()
    {
        var items = JsonSerializer.Deserialize<List<TraktListItem>>(
            TestData.Read("trakt_list_items.json"),
            _jsonOptions);
        Assert.NotNull(items);
        return items!;
    }

    private static OwnershipIndex OwnsTmdb(BaseItemKind kind, params int[] tmdbIds)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in tmdbIds)
        {
            set.Add(OwnershipIndex.MakeKey(kind, "Tmdb", id.ToString(CultureInfo.InvariantCulture)));
        }

        return new OwnershipIndex(set);
    }

    [Fact]
    public void Build_EmitsRecommendationGaps_RoutedByType()
    {
        var gaps = TraktListMapper.Build(ListId, ListName, LoadItems(), OwnsTmdb(BaseItemKind.Movie), 100).ToList();

        // The capture holds nine movies and three shows, each with a distinct tmdb id, so all twelve emit.
        Assert.Equal(12, gaps.Count);
        Assert.Equal(9, gaps.Count(g => g.Domain == MediaDomain.Movies));
        Assert.Equal(3, gaps.Count(g => g.Domain == MediaDomain.Shows));
        Assert.All(gaps, g => Assert.Equal(GapPattern.Recommendation, g.Pattern));
        Assert.All(gaps, g => Assert.Equal("TraktList", g.SourceItemType));
        Assert.All(gaps, g => Assert.Equal(ListName, g.SourceItemName));

        var movie = gaps.Single(g => g.Name == "The Act of Killing");
        Assert.Equal("traktlist:11416887:123678", movie.Id);
        Assert.Equal(MediaDomain.Movies, movie.Domain);
        Assert.Equal(BaseItemKind.Movie, movie.TargetKind);
        Assert.Equal("123678", movie.ProviderIds["Tmdb"]);
        Assert.Equal("tt2375605", movie.ProviderIds["Imdb"]);
        Assert.Equal(2012, movie.Year);

        var show = gaps.Single(g => g.Name == "Worst Ex Ever");
        Assert.Equal(MediaDomain.Shows, show.Domain);
        Assert.Equal(BaseItemKind.Series, show.TargetKind);
        Assert.Equal("259259", show.ProviderIds["Tmdb"]);
    }

    [Fact]
    public void Build_SkipsOwnedByTmdbId()
    {
        var gaps = TraktListMapper.Build(ListId, ListName, LoadItems(), OwnsTmdb(BaseItemKind.Movie, 123678), 100).ToList();

        Assert.DoesNotContain(gaps, g => g.Name == "The Act of Killing");
        Assert.Equal(11, gaps.Count);
    }

    [Fact]
    public void Build_RespectsCap()
    {
        var gaps = TraktListMapper.Build(ListId, ListName, LoadItems(), OwnsTmdb(BaseItemKind.Movie), 1).ToList();

        Assert.Single(gaps);
    }

    // Real response captured from a public Trakt watchlist. Trakt serves a public profile's watchlist with
    // only a client id, no OAuth, which is what makes this source possible. Re-capture with:
    //   curl -s -H "trakt-api-key: <YOUR_CLIENT_ID>" -H "trakt-api-version: 2" \
    //     "https://api.trakt.tv/users/lish408/watchlist/movies,shows?limit=12" -o trakt_watchlist.json
    [Fact]
    public void BuildWatchlist_KeysOnTheUserAndOwnsItsOwnSource()
    {
        var gaps = TraktListMapper.BuildWatchlist("lish408", LoadWatchlist(), OwnsTmdb(BaseItemKind.Movie), 100).ToList();

        Assert.Equal(12, gaps.Count);
        Assert.Equal(11, gaps.Count(g => g.Domain == MediaDomain.Movies));
        Assert.Equal(1, gaps.Count(g => g.Domain == MediaDomain.Shows));
        Assert.All(gaps, g => Assert.Equal(GapPattern.Recommendation, g.Pattern));
        Assert.All(gaps, g => Assert.Equal("TraktWatchlist", g.SourceItemType));
        Assert.All(gaps, g => Assert.Equal("traktwatchlist-lish408", g.SourceItemId));
        Assert.All(gaps, g => Assert.Equal("Trakt watchlist", g.SourceItemName));

        var movie = gaps.Single(g => g.Name == "Cave of Forgotten Dreams");
        Assert.Equal("traktwatchlist:lish408:59490", movie.Id);
        Assert.Equal("59490", movie.ProviderIds["Tmdb"]);
        Assert.Equal("tt1664894", movie.ProviderIds["Imdb"]);
        Assert.Equal(2010, movie.Year);
    }

    [Fact]
    public void BuildWatchlist_SkipsOwnedByTmdbId()
    {
        var gaps = TraktListMapper.BuildWatchlist("lish408", LoadWatchlist(), OwnsTmdb(BaseItemKind.Movie, 59490), 100).ToList();

        Assert.DoesNotContain(gaps, g => g.Name == "Cave of Forgotten Dreams");
        Assert.Equal(11, gaps.Count);
    }

    private static IReadOnlyList<TraktListItem> LoadWatchlist()
    {
        var items = JsonSerializer.Deserialize<List<TraktListItem>>(
            TestData.Read("trakt_watchlist.json"),
            _jsonOptions);
        Assert.NotNull(items);
        return items!;
    }
}
