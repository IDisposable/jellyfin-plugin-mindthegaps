using System.Text.Json;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// TMDB's watch/providers catalog for a region: every provider with its logo. Read once, so an offer stored
// without a logo can be given it without looking its title up again.
public class TmdbProviderLogosTests
{
    private static TmdbProviderList Read(string json)
        => JsonSerializer.Deserialize<TmdbProviderList>(json, TmdbWatchJson.Options)!;

    [Fact]
    public void BuildMap_ReadsTheCatalogAsTMDBSendsIt()
    {
        var catalog = Read("""
            {"results":[
              {"display_priority":0,"logo_path":"/netflix.jpg","provider_name":"Netflix","provider_id":8},
              {"display_priority":1,"logo_path":"/hulu.jpg","provider_name":"Hulu","provider_id":15}
            ]}
            """);

        var map = TmdbProviderLogos.BuildMap([catalog]);

        Assert.Equal("https://image.tmdb.org/t/p/w45/netflix.jpg", map["Netflix"]);
        Assert.Equal("https://image.tmdb.org/t/p/w45/hulu.jpg", map["hulu"]);
    }

    [Fact]
    public void BuildMap_LeavesOutAProviderWithNoLogoOrNoName()
    {
        var catalog = Read("""
            {"results":[
              {"logo_path":null,"provider_name":"No Logo","provider_id":1},
              {"logo_path":"","provider_name":"Empty Logo","provider_id":2},
              {"logo_path":"/x.jpg","provider_name":"","provider_id":3},
              {"logo_path":"/ok.jpg","provider_name":"Kept","provider_id":4}
            ]}
            """);

        var map = TmdbProviderLogos.BuildMap([catalog]);

        Assert.Single(map);
        Assert.True(map.ContainsKey("Kept"));
    }

    [Fact]
    public void BuildMap_FirstCatalogToListAProviderWins_AndTheSecondAddsWhatTheFirstLacks()
    {
        var movies = Read("""{"results":[{"logo_path":"/movie-n.jpg","provider_name":"Netflix","provider_id":8}]}""");
        var series = Read("""{"results":[{"logo_path":"/tv-n.jpg","provider_name":"Netflix","provider_id":8},{"logo_path":"/tv-only.jpg","provider_name":"Only On Series","provider_id":9}]}""");

        var map = TmdbProviderLogos.BuildMap([movies, series]);

        Assert.Equal("https://image.tmdb.org/t/p/w45/movie-n.jpg", map["Netflix"]);
        Assert.Equal("https://image.tmdb.org/t/p/w45/tv-only.jpg", map["Only On Series"]);
    }

    [Fact]
    public void BuildMap_ToleratesAMissingCatalog_ThatFailedToLoad()
    {
        var series = Read("""{"results":[{"logo_path":"/n.jpg","provider_name":"Netflix","provider_id":8}]}""");

        var map = TmdbProviderLogos.BuildMap([null, series]);

        Assert.Single(map);
        Assert.Empty(TmdbProviderLogos.BuildMap([null, null]));
        Assert.Empty(TmdbProviderLogos.BuildMap([Read("{}")]));
    }
}
