using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Newtonsoft.Json;
using Xunit;
using TmdbPerson = TMDbLib.Objects.People.Person;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Real response captured from
// api.themoviedb.org/3/person/24?append_to_response=movie_credits,tv_credits (Robert Zemeckis).
// The capture carries both movie_credits and tv_credits, so the movie and TV filmography paths run
// against the same real response.
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

        var gaps = FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", ownership, Poster, 0, 0).ToList();

        // Movie gaps: Cast 26 + Directing 31 + Writing 21 = 78. Production (40) and Crew (6) are filtered out.
        // If the department filter regressed, all 98 crew credits would pass: 26 + 98 = 124, capped at 100.
        var movies = gaps.Where(g => g.TargetKind == BaseItemKind.Movie).ToList();
        Assert.Equal(78, movies.Count);
        Assert.Contains(movies, g => g.Id == "filmography:movie:13");        // Forrest Gump (Directing) kept
        Assert.DoesNotContain(movies, g => g.Id == "filmography:movie:10066"); // House of Wax (Production) dropped
        Assert.All(gaps, g =>
        {
            Assert.Equal(GapPattern.CreatorWorks, g.Pattern);
            Assert.Equal("Person", g.SourceItemType);
        });
        Assert.All(movies, g => Assert.Equal(BaseItemKind.Movie, g.TargetKind));
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

        var gaps = FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", ownership, Poster, 0, 0).ToList();
        var movies = gaps.Where(g => g.TargetKind == BaseItemKind.Movie).ToList();

        Assert.Equal(77, movies.Count);
        Assert.DoesNotContain(movies, g => g.Id == "filmography:movie:13");
    }

    [Fact]
    public void Build_VoteGate_DropsObscureCredits_KeepsNotableOnes()
    {
        var person = Load();
        var ownership = new OwnershipIndex(new Dictionary<string, BaseItem>());

        // With a 100-vote floor the obscure cast credits fall away (Zemeckis is a director, so his cast
        // roles are mostly cameos with few votes): cast 26 falls to 4. The 52 directing/writing crew credits
        // are not vote-gated, so 4 + 52 = 56.
        var gated = FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", ownership, Poster, 100, 0)
            .Where(g => g.TargetKind == BaseItemKind.Movie).ToList();
        var all = FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", ownership, Poster, 0, 0)
            .Where(g => g.TargetKind == BaseItemKind.Movie).ToList();

        Assert.True(gated.Count < all.Count);
        Assert.Equal(56, gated.Count);
        Assert.Contains(gated, g => g.Id == "filmography:movie:13"); // Forrest Gump (Directing) kept
    }

    [Fact]
    public void Build_CastBillingGate_DropsDeeplyBilledRoles()
    {
        var person = Load();
        var ownership = new OwnershipIndex(new Dictionary<string, BaseItem>());

        var noLimit = FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", ownership, Poster, 0, 0).ToList();
        var topBilled = FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", ownership, Poster, 0, 5).ToList();

        // Crew credits are unaffected by billing, so the drop comes entirely from deeply-billed cast roles.
        Assert.True(topBilled.Count < noLimit.Count);
    }

    [Fact]
    public void Build_UnownedTvCredit_BecomesShowsSeriesCreatorWorksGap()
    {
        var person = Load();
        var ownership = new OwnershipIndex(new Dictionary<string, BaseItem>());

        // No vote floor, so the TV cast roles surface alongside the directing/writing crew credits.
        var gaps = FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", ownership, Poster, 0, 0).ToList();

        var series = gaps.Where(g => g.TargetKind == BaseItemKind.Series).ToList();

        Assert.All(series, g =>
        {
            Assert.Equal(GapPattern.CreatorWorks, g.Pattern);
            Assert.Equal(MediaDomain.Shows, g.Domain);
            Assert.Equal(BaseItemKind.Series, g.TargetKind);
            Assert.Equal("Person", g.SourceItemType);
        });
        Assert.Contains(series, g => g.Id == "filmography:series:63770"); // The Late Show (cast) kept
        Assert.Contains(series, g => g.Id == "filmography:series:1026");  // Amazing Stories (Directing) kept
        Assert.DoesNotContain(series, g => g.Id == "filmography:series:79649"); // Project Blue Book (Production) dropped
    }

    [Fact]
    public void Build_OwnedTvCredit_Excluded()
    {
        var person = Load();
        var owned = new Dictionary<string, BaseItem>
        {
            [OwnershipIndex.MakeKey(BaseItemKind.Series, "Tmdb", "1026")] = new Series()
        };
        var ownership = new OwnershipIndex(owned);

        var gaps = FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", ownership, Poster, 0, 0).ToList();

        Assert.DoesNotContain(gaps, g => g.Id == "filmography:series:1026");   // owned Amazing Stories excluded
        Assert.Contains(gaps, g => g.Id == "filmography:series:63770");        // other series still surface
    }

    [Fact]
    public void Build_VoteFloor_DropsTvCastButKeepsTvCrew()
    {
        var person = Load();
        var ownership = new OwnershipIndex(new Dictionary<string, BaseItem>());

        // TV filmography roles carry no vote count, so a positive vote floor drops every TV cast role while
        // the directing/writing crew is not vote-gated, exactly as on the movie path.
        var gaps = FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", ownership, Poster, 100, 0).ToList();

        Assert.DoesNotContain(gaps, g => g.Id == "filmography:series:63770"); // The Late Show (cast) dropped by the floor
        Assert.Contains(gaps, g => g.Id == "filmography:series:1026");        // Amazing Stories (Directing) kept
    }
}
