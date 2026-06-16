using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Newtonsoft.Json;
using Xunit;
using TmdbPerson = TMDbLib.Objects.People.Person;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Real response captured from api.themoviedb.org/3/person/24?append_to_response=movie_credits (Robert Zemeckis).
public class TmdbFilmographyCapturedDataTests
{
    private static TmdbPerson Load()
        => JsonConvert.DeserializeObject<TmdbPerson>(TestData.Read("tmdb_person.json"))!;

    private static string? Poster(string? path) => path;

    [Fact]
    public void Build_KeepsCastAndDirectingWriting_DropsProductionAndOtherCrew()
    {
        var person = Load();
        var ownership = new OwnershipIndex(new Dictionary<string, BaseItem>());

        var gaps = FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", ownership, Poster).ToList();

        // Cast 26 + Directing 31 + Writing 22 = 79. Production (40) and Crew (6) are filtered out.
        // If the department filter regressed, all 99 crew credits would pass: 26 + 99 = 125, capped at 100.
        Assert.Equal(79, gaps.Count);
        Assert.Contains(gaps, g => g.Id == "filmography:movie:13");        // Forrest Gump (Directing) kept
        Assert.DoesNotContain(gaps, g => g.Id == "filmography:movie:10066"); // House of Wax (Production) dropped
        Assert.All(gaps, g =>
        {
            Assert.Equal(GapPattern.CreatorWorks, g.Pattern);
            Assert.Equal(BaseItemKind.Movie, g.TargetKind);
            Assert.Equal("Person", g.SourceItemType);
        });
    }

    [Fact]
    public void Build_OwnedCreditExcluded()
    {
        var person = Load();
        var owned = new Dictionary<string, BaseItem>
        {
            [OwnershipIndex.MakeKey(BaseItemKind.Movie, "Tmdb", "13")] = new Movie()
        };
        var ownership = new OwnershipIndex(owned);

        var gaps = FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", ownership, Poster).ToList();

        Assert.Equal(78, gaps.Count);
        Assert.DoesNotContain(gaps, g => g.Id == "filmography:movie:13");
    }
}
