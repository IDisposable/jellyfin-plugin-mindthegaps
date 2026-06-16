using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Trakt;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Trakt;
using MediaBrowser.Controller.Entities;
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

        var ownership = new OwnershipIndex(new Dictionary<string, BaseItem>());
        var gaps = TraktFilmographyMapper.Build(credits!, "person-id", "Robert Zemeckis", ownership).ToList();

        Assert.NotEmpty(gaps);
        Assert.All(gaps, g =>
        {
            Assert.Equal(GapPattern.CreatorWorks, g.Pattern);
            Assert.Equal(BaseItemKind.Movie, g.TargetKind);
            Assert.StartsWith("filmography:movie:", g.Id, System.StringComparison.Ordinal);
        });
    }
}
