using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Imdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Imdb;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Real responses captured from the GraphQL API imdb.com reads from. It needs no key, only the client-name
// header, and serves what an account has published, so both captures are of public data.
//
// imdb_list_items.json is a public list that mixes movie, tvMovie, and tvMiniSeries, which exercises both the
// Movies and the Shows routing. Re-capture with:
//   curl -s -X POST https://api.graphql.imdb.com/ -H 'Content-Type: application/json' \
//     -H 'x-imdb-client-name: mind-the-gaps' \
//     -d '{"query":"query MtgList($id: ID!, $first: Int!, $after: ID) { list(id: $id) { id name { originalText } items(first: $first, after: $after) { total pageInfo { hasNextPage endCursor } edges { node { item { __typename ... on Title { id titleText { text } releaseYear { year } titleType { id canHaveEpisodes } primaryImage { url } } } } } } } }","variables":{"id":"ls022958322","first":40}}' \
//     -o imdb_list_items.json
//
// imdb_watchlist_envelope.json is the watchlist query's envelope, captured with first=0 so it holds the shape
// (the predefinedList root field, the list's own "ls" id) and none of that account's titles. Re-capture with
// the same call, swapping the query for the MtgWatchlist one in ImdbClient and passing {"id":"ur1000000","first":0}.
public class ImdbListTests
{
    private const string ListId = "ls022958322";
    private const string ListName = "L-Movies";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Build_EmitsRecommendationGaps_RoutedByTitleType()
    {
        var gaps = ImdbListMapper.Build(ListId, ListName, LoadTitles(), Owns(), 100).ToList();

        // The capture holds 37 movies, one tvMovie, and two tvMiniSeries; only the episodic pair is a Series.
        Assert.Equal(40, gaps.Count);
        Assert.Equal(38, gaps.Count(g => g.Domain == MediaDomain.Movies));
        Assert.Equal(2, gaps.Count(g => g.Domain == MediaDomain.Shows));
        Assert.All(gaps, g => Assert.Equal(GapPattern.Recommendation, g.Pattern));
        Assert.All(gaps, g => Assert.Equal("ImdbList", g.SourceItemType));
        Assert.All(gaps, g => Assert.Equal(ListName, g.SourceItemName));
        Assert.All(gaps, g => Assert.StartsWith("imdblist:ls022958322:tt", g.Id, System.StringComparison.Ordinal));
    }

    [Fact]
    public void Build_CarriesImdbIdAndYear()
    {
        var gaps = ImdbListMapper.Build(ListId, ListName, LoadTitles(), Owns(), 100).ToList();

        var movie = gaps.First(g => g.TargetKind == BaseItemKind.Movie);
        Assert.StartsWith("tt", movie.ProviderIds["Imdb"], System.StringComparison.Ordinal);
        Assert.NotNull(movie.Year);

        // Every entry links out, built from the IMDb id alone.
        Assert.All(gaps, g => Assert.Contains(g.Links, l => l.Name == "IMDb"));
    }

    [Fact]
    public void Build_SkipsOwnedByImdbId()
    {
        var titles = LoadTitles();
        var owned = titles.First(t => t.TitleType?.CanHaveEpisodes != true).Id!;

        var gaps = ImdbListMapper.Build(ListId, ListName, titles, Owns(BaseItemKind.Movie, owned), 100).ToList();

        Assert.DoesNotContain(gaps, g => g.ProviderIds["Imdb"] == owned);
        Assert.Equal(39, gaps.Count);
    }

    [Fact]
    public void Build_RespectsCap()
    {
        var gaps = ImdbListMapper.Build(ListId, ListName, LoadTitles(), Owns(), 3).ToList();

        Assert.Equal(3, gaps.Count);
    }

    [Fact]
    public void WatchlistQuery_ReturnsAListUnderItsOwnId()
    {
        var response = JsonSerializer.Deserialize<ImdbGraphResponse>(
            TestData.Read("imdb_watchlist_envelope.json"),
            _jsonOptions);

        // A watchlist is a list: it comes back under predefinedList, with its own "ls" id and a total, which
        // is what lets one mapper and one paging loop serve both queries.
        Assert.NotNull(response?.Data?.PredefinedList);
        Assert.Null(response!.Data!.List);
        Assert.StartsWith("ls", response.Data.PredefinedList!.Id, System.StringComparison.Ordinal);
        Assert.Equal("WATCHLIST", response.Data.PredefinedList.Name?.Value);
        Assert.True(response.Data.PredefinedList.Items?.Total > 0);
    }

    [Fact]
    public void AListSaysWhetherItHoldsTitlesOrPeople()
    {
        // The served list type is what routes a list to the titles source or the people source, so neither
        // has to guess from the entries.
        Assert.Equal(ImdbListContents.TitlesType, LoadList("imdb_list_items.json").ListType?.Id);
        Assert.Equal(ImdbListContents.PeopleType, LoadList("imdb_people_list.json").ListType?.Id);
    }

    private static ImdbList LoadList(string fixture)
    {
        var response = JsonSerializer.Deserialize<ImdbGraphResponse>(TestData.Read(fixture), _jsonOptions);
        Assert.NotNull(response?.Data?.List);
        return response!.Data!.List!;
    }

    private static IReadOnlyList<ImdbListEntry> LoadTitles()
    {
        var edges = LoadList("imdb_list_items.json").Items?.Edges;
        Assert.NotNull(edges);
        return edges!.Select(e => e.Node?.Item).Where(t => t is not null).Select(t => t!).ToList();
    }

    private static OwnershipIndex Owns(BaseItemKind kind = BaseItemKind.Movie, params string[] imdbIds)
    {
        var dict = new Dictionary<string, BaseItem>();
        foreach (var id in imdbIds)
        {
            dict[OwnershipIndex.MakeKey(kind, "Imdb", id)] = null!;
        }

        return new OwnershipIndex(dict);
    }
}
