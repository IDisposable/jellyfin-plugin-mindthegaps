using System.Text.Json;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Services.Tvdb;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Real response captured from TheTVDB v4. A favourite is account data, so the capture needs a token minted
// from the API key AND a subscriber PIN; a key-only token is not tied to an account and is refused. The
// series record itself is plain catalog data. Re-capture with:
//   TOKEN=$(curl -s -X POST https://api4.thetvdb.com/v4/login -H 'Content-Type: application/json' \
//     -d '{"apikey":"<YOUR_KEY>","pin":"<YOUR_PIN>"}' | jq -r .data.token)
//   curl -s -H "Authorization: Bearer $TOKEN" https://api4.thetvdb.com/v4/series/387219 -o tvdb_series.json
// The response carries no credential, so it is safe to commit.
public class TvdbFavoritesTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void SeriesRecord_CarriesWhatAFavouriteRowNeeds()
    {
        // A favourite arrives as a bare id, so the record is the only source of a name, a date, and a poster.
        var response = JsonSerializer.Deserialize<TvdbSeriesResponse>(TestData.Read("tvdb_series.json"), _jsonOptions);

        Assert.NotNull(response?.Data);
        Assert.Equal(387219, response!.Data!.Id);
        Assert.Equal("Formula 1", response.Data.Name);
        Assert.Equal("1950-05-13", response.Data.FirstAired);
        Assert.StartsWith("https://artworks.thetvdb.com/", response.Data.Image, System.StringComparison.Ordinal);
    }

    [Fact]
    public void TheGapIdIsKeyedOnTheSeriesIdAlone()
    {
        // There is one favourites set per account, so the id needs no owner segment.
        Assert.Equal("tvdbfavorite:", GapIdPrefixes.TvdbFavorite);
        Assert.Equal("tvdb-favorites", SourceItemIds.TvdbFavorites);
    }
}
