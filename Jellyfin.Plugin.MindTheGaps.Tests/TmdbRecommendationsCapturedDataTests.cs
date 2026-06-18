using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using Newtonsoft.Json;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Search;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Real response captured from api.themoviedb.org/3/movie/101/similar (Leon: The Professional).
public class TmdbRecommendationsCapturedDataTests
{
    private static SearchContainer<SearchMovie> Load()
        => JsonConvert.DeserializeObject<SearchContainer<SearchMovie>>(TestData.Read("tmdb_similar.json"))!;

    private static string? Poster(string? path) => path;

    [Fact]
    public void BuildMovies_RespectsPerItemCap()
    {
        var similar = Load();
        var ownership = new OwnershipIndex(new Dictionary<string, BaseItem>());

        var gaps = RecommendationGapMapper.BuildMovies(similar.Results!, "leon", "Leon", 1994, ownership, Poster, 5, 0).ToList();

        Assert.Equal(5, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(GapPattern.Recommendation, g.Pattern));
        Assert.All(gaps, g => Assert.Equal(1994, g.SourceItemYear));
    }

    [Fact]
    public void BuildMovies_EmitsAllUnownedResults()
    {
        var similar = Load();
        var ownership = new OwnershipIndex(new Dictionary<string, BaseItem>());

        var gaps = RecommendationGapMapper.BuildMovies(similar.Results!, "leon", "Leon", 1994, ownership, Poster, 100, 0).ToList();

        Assert.Equal(20, gaps.Count);
        Assert.Equal("recommendation:movie:1969", gaps[0].Id);
    }

    [Fact]
    public void BuildMovies_VoteGate_DropsObscureSuggestions()
    {
        var similar = Load();
        var ownership = new OwnershipIndex(new Dictionary<string, BaseItem>());

        // 7 of the 20 captured results have at least 1000 TMDB votes (the obscure long tail falls away).
        var gaps = RecommendationGapMapper.BuildMovies(similar.Results!, "leon", "Leon", 1994, ownership, Poster, 100, 1000).ToList();

        Assert.Equal(7, gaps.Count);
        Assert.Contains(gaps, g => g.Id == "recommendation:movie:1969"); // 1174 votes, kept
    }
}
