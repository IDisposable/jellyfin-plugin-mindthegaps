using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.JustWatch;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.JustWatch;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// justwatch_watchlist.json is the watchlist response assembled around real captured nodes. A watchlist read
// needs the account's own bearer token, so the connection envelope (totalCount, pageInfo, edges) cannot be
// captured without one and is written out here; every node inside it is a verbatim capture from the public
// urlV2 query, which returns the same MovieOrShow type the watchlist edges hold. The query itself is verified
// against the live endpoint: it passes validation unauthenticated and fails only on authorization.
//
// Re-capture the whole response, signed in, with the token from the browser's Network tab:
//   curl -s -X POST https://apis.justwatch.com/graphql -H 'Content-Type: application/json' \
//     -H 'Origin: https://www.justwatch.com' -H "Authorization: Bearer <YOUR_TOKEN>" \
//     -d '{"query":"query MtgTitleList($country: Country!, $language: Language!, $listType: TitleListTypeV2!, $first: Int!, $after: String) { titleListV2(country: $country, titleListType: $listType, first: $first, after: $after) { totalCount pageInfo { hasNextPage endCursor } edges { node { __typename ... on MovieOrShow { id objectType content(country: $country, language: $language) { title originalReleaseYear fullPath posterUrl externalIds { imdbId tmdbId } } } } } } }","variables":{"country":"US","language":"en","listType":"WATCHLIST","first":100}}' \
//     -o justwatch_watchlist.json
// A re-captured file holds that account's titles, so scrub it before committing.
//
// Re-capture one node (no token needed) with:
//   curl -s -X POST https://apis.justwatch.com/graphql -H 'Content-Type: application/json' \
//     -H 'Origin: https://www.justwatch.com' \
//     -d '{"query":"query N($fullPath: String!, $country: Country!, $language: Language!) { urlV2(fullPath: $fullPath) { node { __typename ... on MovieOrShow { id objectType content(country: $country, language: $language) { title originalReleaseYear fullPath posterUrl externalIds { imdbId tmdbId } } } } } }","variables":{"fullPath":"/us/movie/the-matrix","country":"US","language":"en"}}'
public class JustWatchListTests
{
    private const string ListName = "JustWatch watchlist";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Build_EmitsRecommendationGaps_RoutedByObjectType()
    {
        var gaps = Build(Owns());

        // Three entries, but the one JustWatch holds no external ids for cannot be diffed and is dropped.
        Assert.Equal(2, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(GapPattern.Recommendation, g.Pattern));
        Assert.All(gaps, g => Assert.Equal("JustWatchList", g.SourceItemType));
        Assert.All(gaps, g => Assert.Equal(ListName, g.SourceItemName));

        var movie = gaps.Single(g => g.Name == "The Matrix");
        Assert.Equal("justwatch:watchlist:603", movie.Id);
        Assert.Equal(MediaDomain.Movies, movie.Domain);
        Assert.Equal(BaseItemKind.Movie, movie.TargetKind);
        Assert.Equal("603", movie.ProviderIds["Tmdb"]);
        Assert.Equal("tt0133093", movie.ProviderIds["Imdb"]);
        Assert.Equal(1999, movie.Year);

        var show = gaps.Single(g => g.Name == "Breaking Bad");
        Assert.Equal(MediaDomain.Shows, show.Domain);
        Assert.Equal(BaseItemKind.Series, show.TargetKind);
        Assert.Equal("1396", show.ProviderIds["Tmdb"]);
    }

    [Fact]
    public void Build_ExpandsThePosterTemplateAndLinksBack()
    {
        var movie = Build(Owns()).Single(g => g.Name == "The Matrix");

        // The API returns a template, not a URL: the {profile} size and {format} extension are filled in.
        Assert.Equal("https://images.justwatch.com/poster/126401284/s332/the-matrix.jpg", movie.ImageUrl);
        Assert.Contains(movie.Links, l => l.Name == "JustWatch" && l.Url == "https://www.justwatch.com/us/movie/the-matrix");
    }

    [Fact]
    public void Build_SkipsOwnedByTmdbId()
    {
        var gaps = Build(Owns(BaseItemKind.Movie, "603"));

        Assert.DoesNotContain(gaps, g => g.Name == "The Matrix");
        Assert.Single(gaps);
    }

    [Fact]
    public void Build_RespectsCap()
    {
        Assert.Single(BuildCapped(1));
    }

    [Fact]
    public void ListType_NamesOnlyTheWantLists()
    {
        // A seen list is the opposite of a gap, so it is deliberately not readable.
        Assert.Equal(new[] { "WATCHLIST", "LIKELIST" }, JustWatchListType.All);
        Assert.False(JustWatchListType.IsKnown("SEENLIST"));
        Assert.Equal("JustWatch watchlist", JustWatchListType.DisplayName(JustWatchListType.Watchlist));
        Assert.Equal("JustWatch likes", JustWatchListType.DisplayName(JustWatchListType.Likelist));
    }

    private static List<GapItem> Build(OwnershipIndex ownership)
        => JustWatchListMapper.Build(JustWatchListType.Watchlist, ListName, LoadTitles(), ownership, 100).ToList();

    private static List<GapItem> BuildCapped(int max)
        => JustWatchListMapper.Build(JustWatchListType.Watchlist, ListName, LoadTitles(), Owns(), max).ToList();

    private static IReadOnlyList<JustWatchTitle> LoadTitles()
    {
        var response = JsonSerializer.Deserialize<JustWatchGraphResponse>(
            TestData.Read("justwatch_watchlist.json"),
            _jsonOptions);
        var edges = response?.Data?.TitleListV2?.Edges;
        Assert.NotNull(edges);
        return edges!.Select(e => e.Node).Where(t => t is not null).Select(t => t!).ToList();
    }

    private static OwnershipIndex Owns(BaseItemKind kind = BaseItemKind.Movie, params string[] tmdbIds)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in tmdbIds)
        {
            set.Add(OwnershipIndex.MakeKey(kind, "Tmdb", id.ToString(CultureInfo.InvariantCulture)));
        }

        return new OwnershipIndex(set);
    }
}
