using System.Text.Json;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Real response captured from api.themoviedb.org/3/movie/101/watch/providers (Leon: The Professional).
public class TmdbAvailabilityCapturedDataTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private static TmdbWatchResponse? Load()
        => JsonSerializer.Deserialize<TmdbWatchResponse>(TestData.Read("tmdb_watchproviders.json"), Options);

    [Fact]
    public void Map_CountryWithOffers_ProducesOffers()
    {
        var offers = TmdbWatchMapper.Map(Load(), "AE");

        Assert.Equal(2, offers.Count);
        Assert.All(offers, o => Assert.Equal("Google Play Movies", o.Provider));
        Assert.Contains(offers, o => o.MonetizationType == "rent");
        Assert.Contains(offers, o => o.MonetizationType == "buy");
        Assert.All(offers, o => Assert.Contains("locale=AE", o.Url!, System.StringComparison.Ordinal));
    }

    [Fact]
    public void Map_UnknownCountry_IsEmpty()
    {
        Assert.Empty(TmdbWatchMapper.Map(Load(), "ZZ"));
    }
}
