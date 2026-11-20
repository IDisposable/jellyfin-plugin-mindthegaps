using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Trakt;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Trakt;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Real response captured from api.trakt.tv/people/{id}/movies?extended=full.
// Trakt requires a trakt-api-key header (your own client id), so unlike the other sources this one
// can't be captured without a key. Once you have a client id, capture the fixture (Robert Zemeckis
// keeps this aligned with the TMDB filmography test):
//   curl -s -H "trakt-api-key: <YOUR_CLIENT_ID>" -H "trakt-api-version: 2" \
//     "https://api.trakt.tv/people/robert-zemeckis/movies?extended=full" \
//     -o Jellyfin.Plugin.MindTheGaps.Tests/TestData/trakt_person_movies.json
// then remove the Skip argument below.
public class TraktFilmographyCapturedDataTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Build_ParsesCreditsAndProducesFilmographyGaps()
    {
        var credits = JsonSerializer.Deserialize<TraktPersonMovieCredits>(TestData.Read("trakt_person_movies.json"), Options);
        Assert.NotNull(credits);

        var ownership = new OwnershipIndex(new HashSet<string>(StringComparer.Ordinal));
        var gaps = TraktFilmographyMapper.Build(credits!, "person-id", "Robert Zemeckis", ownership).ToList();

        Assert.NotEmpty(gaps);
        Assert.All(gaps, g =>
        {
            Assert.Equal(GapPattern.CreatorWorks, g.Pattern);
            Assert.Equal(BaseItemKind.Movie, g.TargetKind);
            Assert.StartsWith("filmography:movie:", g.Id, System.StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Build_KeepsTheSlug_LinksByImdbFirst_AndSkipsACreditWithNoTmdbOrImdbId()
    {
        var credits = new TraktPersonMovieCredits
        {
            Cast =
            [
                new TraktMovieCastCredit { Movie = new TraktMovie { Title = "No Imdb", Year = 2020, Ids = new TraktIds { Tmdb = 123, Slug = "no-imdb-2020" } } },
                new TraktMovieCastCredit { Movie = new TraktMovie { Title = "Both", Year = 2022, Ids = new TraktIds { Tmdb = 456, Imdb = "tt0000456", Slug = "both-2022" } } },
                new TraktMovieCastCredit { Movie = new TraktMovie { Title = "Slug Only", Year = 2021, Ids = new TraktIds { Slug = "slug-only-2021" } } }
            ]
        };

        var gaps = TraktFilmographyMapper.Build(credits, "person-id", "Someone", new OwnershipIndex(new HashSet<string>(StringComparer.Ordinal))).ToList();

        Assert.Equal(["filmography:movie:123", "filmography:movie:456"], gaps.Select(g => g.Id));
        var noImdb = gaps[0];
        var both = gaps[1];
        Assert.Equal("no-imdb-2020", noImdb.ProviderIds[ProviderIds.TraktSlug]);
        Assert.Equal("both-2022", both.ProviderIds[ProviderIds.TraktSlug]);
        Assert.Contains(ProviderLinks.Build(BaseItemKind.Movie, noImdb.ProviderIds, trakt: true), l => l.Url == "https://trakt.tv/movies/no-imdb-2020");
        Assert.Contains(ProviderLinks.Build(BaseItemKind.Movie, both.ProviderIds, trakt: true), l => l.Url == "https://trakt.tv/movies/tt0000456");
        Assert.DoesNotContain(ProviderLinks.Build(BaseItemKind.Movie, noImdb.ProviderIds, trakt: false), l => l.Name == "Trakt");
    }
}
