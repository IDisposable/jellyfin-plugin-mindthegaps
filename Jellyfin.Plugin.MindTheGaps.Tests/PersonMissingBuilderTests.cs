using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Newtonsoft.Json;
using Xunit;
using TmdbPerson = TMDbLib.Objects.People.Person;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Runs the person page's pure half over the same captured TMDB person the filmography mapper tests use
// (Robert Zemeckis, tmdb_person.json), so the page and the report agree on what a credit is.
public class PersonMissingBuilderTests
{
    private static readonly IReadOnlyDictionary<string, GapResolution> _noResolutions = new Dictionary<string, GapResolution>(StringComparer.Ordinal);

    private static List<GapItem> Gaps(params string[] ownedMovieTmdbIds)
    {
        var person = JsonConvert.DeserializeObject<TmdbPerson>(TestData.Read("tmdb_person.json"))!;
        var keys = new HashSet<string>(ownedMovieTmdbIds.Select(id => OwnershipIndex.MakeKey(BaseItemKind.Movie, "Tmdb", id)), StringComparer.Ordinal);
        return FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", new OwnershipIndex(keys), p => p, 0, 0, maxCredits: int.MaxValue).ToList();
    }

    [Fact]
    public void Split_DropsSelfAppearances_KeepsRealCreditsAndCrew()
    {
        var gaps = Gaps();
        var (movies, series) = PersonMissingBuilder.Split(gaps, "Robert Zemeckis", _noResolutions);

        // The capture's 26 movie cast credits are 24 "Self" documentary appearances plus 2 real roles;
        // directing/writing crew (52 titles, some overlapping cast) is never a self-appearance.
        Assert.DoesNotContain(movies, m => m.Role is not null && m.Role.StartsWith("as Self", StringComparison.Ordinal));
        Assert.DoesNotContain(movies, m => m.Role is not null && m.Role.StartsWith("as Himself", StringComparison.Ordinal));
        Assert.Contains(movies, m => m.GapId == "filmography:movie:13" && m.Role == "Director"); // Forrest Gump
        // 52 directing/writing credits plus 2 real roles collapse to 36 distinct titles once merged.
        Assert.Equal(36, movies.Count);

        // 6 of the 7 TV cast credits are "Self" (MADtv, The Oscars, ...); the seventh is a character named
        // "Robert Zemeckis" on Parker Lewis Can't Lose, which is the person as themself too.
        Assert.DoesNotContain(series, s => s.TmdbId == 4469);
        Assert.DoesNotContain(series, s => s.TmdbId == 3089);
    }

    [Fact]
    public void Split_OrdersNewestFirst_UndatedLast()
    {
        var (movies, _) = PersonMissingBuilder.Split(Gaps(), "Robert Zemeckis", _noResolutions);

        var dated = movies.Where(m => m.ReleaseDate is not null).Select(m => m.ReleaseDate!.Value).ToList();
        Assert.True(dated.Count > 1);
        Assert.Equal(dated.OrderByDescending(d => d).ToList(), dated);
        var firstUndated = movies.ToList().FindIndex(m => m.ReleaseDate is null);
        if (firstUndated >= 0)
        {
            Assert.All(movies.Skip(firstUndated), m => Assert.Null(m.ReleaseDate));
        }
    }

    [Fact]
    public void Split_HidesDismissedGaps_AndOwnedTitlesNeverArrive()
    {
        // Owned titles are already gone before Split (the mapper checks ownership); a dismissal hides a gap
        // the same way the report does.
        var gaps = Gaps("13");
        Assert.DoesNotContain(gaps, g => g.Id == "filmography:movie:13");

        var dismissed = gaps.First(g => g.TargetKind == BaseItemKind.Movie);
        var resolutions = new Dictionary<string, GapResolution>(StringComparer.Ordinal)
        {
            [dismissed.Id] = new GapResolution { Kind = GapResolution.NotInterested }
        };

        var (movies, _) = PersonMissingBuilder.Split(gaps, "Robert Zemeckis", resolutions);
        Assert.DoesNotContain(movies, m => m.GapId == dismissed.Id);
    }

    [Fact]
    public void Split_CarriesTheFieldsThePageRenders()
    {
        var (movies, _) = PersonMissingBuilder.Split(Gaps(), "Robert Zemeckis", _noResolutions);
        var gump = movies.Single(m => m.GapId == "filmography:movie:13");

        Assert.Equal("Forrest Gump", gump.Title);
        Assert.Equal(13, gump.TmdbId);
        Assert.Equal(1994, gump.Year);
        Assert.False(gump.Upcoming);
        Assert.NotNull(gump.ImageUrl);
    }

    [Fact]
    public void Uncapped_ReachesTvCredits_ThatTheScanCapWouldNotFor_APersonWithManyMovieCredits()
    {
        // Zemeckis has 78 movie gaps and 7 TV credits; both fit under the scan cap, so the point is made
        // with a cap smaller than the movie count: capped, no series survive; uncapped, the TV credits arrive.
        var person = JsonConvert.DeserializeObject<TmdbPerson>(TestData.Read("tmdb_person.json"))!;
        var none = new OwnershipIndex(new HashSet<string>(StringComparer.Ordinal));

        var capped = FilmographyGapMapper.Build(person, "p", "Robert Zemeckis", none, p => p, 0, 0, maxCredits: 20).ToList();
        Assert.Equal(20, capped.Count);
        Assert.DoesNotContain(capped, g => g.TargetKind == BaseItemKind.Series);

        var all = FilmographyGapMapper.Build(person, "p", "Robert Zemeckis", none, p => p, 0, 0, maxCredits: int.MaxValue).ToList();
        Assert.Contains(all, g => g.TargetKind == BaseItemKind.Series);
        Assert.True(all.Count > GapScanLimits.MaxCreditsPerPerson - 20, $"{all.Count}");

        // The default is still the scan cap.
        var scan = FilmographyGapMapper.Build(person, "p", "Robert Zemeckis", none, p => p, 0, 0).ToList();
        Assert.True(scan.Count <= GapScanLimits.MaxCreditsPerPerson);
    }

    [Fact]
    public void Split_MergesCastAndCrewCreditsOnOneTitle()
    {
        // Zemeckis both directed and wrote many of his films: the mapper emits a gap per credit under the
        // same id, and the page shows the title once with both credits.
        var gaps = Gaps();
        var dupIds = gaps.Where(g => g.TargetKind == BaseItemKind.Movie).GroupBy(g => g.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.NotEmpty(dupIds);

        var (movies, _) = PersonMissingBuilder.Split(gaps, "Robert Zemeckis", _noResolutions);
        Assert.Equal(movies.Count, movies.Select(m => m.GapId).Distinct().Count());
        var merged = movies.Single(m => m.GapId == dupIds[0]);
        Assert.Contains(" \u00b7 ", merged.Role, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "Director", "Director")]
    [InlineData("Director", null, "Director")]
    [InlineData("Director", "Director", "Director")]
    [InlineData("as Hal", "Director", "as Hal \u00b7 Director")]
    [InlineData("as Hal \u00b7 Director", "director", "as Hal \u00b7 Director")]
    [InlineData("as Hal \u00b7 Director", "Story", "as Hal \u00b7 Director \u00b7 Story")]
    public void MergeRoles_AppendsNewCreditsOnly(string? first, string? second, string? expected)
        => Assert.Equal(expected, MissingTitleBuilder.MergeRoles(first, second));

    [Fact]
    public void MinTvEpisodes_DropsOneEpisodeCredits_KeepsUnknownCounts()
    {
        var person = JsonConvert.DeserializeObject<TmdbPerson>(TestData.Read("tmdb_person.json"))!;
        var none = new OwnershipIndex(new HashSet<string>(StringComparer.Ordinal));

        // Every TV cast credit in the capture spans 1 or 2 episodes.
        var all = FilmographyGapMapper.Build(person, "p", "Robert Zemeckis", none, p => p, 0, 0, int.MaxValue, minTvEpisodes: 0)
            .Count(g => g.TargetKind == BaseItemKind.Series && g.Overview is not null && g.Overview.StartsWith("as ", StringComparison.Ordinal));
        var atLeastTwo = FilmographyGapMapper.Build(person, "p", "Robert Zemeckis", none, p => p, 0, 0, int.MaxValue, minTvEpisodes: 2)
            .Count(g => g.TargetKind == BaseItemKind.Series && g.Overview is not null && g.Overview.StartsWith("as ", StringComparison.Ordinal));
        var atLeastThree = FilmographyGapMapper.Build(person, "p", "Robert Zemeckis", none, p => p, 0, 0, int.MaxValue, minTvEpisodes: 3)
            .Count(g => g.TargetKind == BaseItemKind.Series && g.Overview is not null && g.Overview.StartsWith("as ", StringComparison.Ordinal));

        Assert.Equal(7, all);
        Assert.Equal(1, atLeastTwo);   // The Oscars, 2 episodes
        Assert.Equal(0, atLeastThree);
    }

    [Theory]
    [InlineData("as Self", null, true)]
    [InlineData("as Himself", null, true)]
    [InlineData("as Herself", null, true)]
    [InlineData("as Self - Host", null, true)]
    [InlineData("as Himself (archive footage)", null, true)]
    [InlineData("as Selfridge", null, false)]
    [InlineData("as Marty McFly", null, false)]
    [InlineData("Director", null, false)]
    [InlineData("as Robert Zemeckis", "Robert Zemeckis", true)]
    [InlineData("as robert zemeckis", "Robert Zemeckis", true)]
    [InlineData("as Robert Zemeckis", "Someone Else", false)]
    [InlineData(null, "Robert Zemeckis", false)]
    [InlineData("", null, false)]
    public void IsSelfAppearance_MatchesSelfAndOwnName_NotPrefixes(string? role, string? personName, bool expected)
        => Assert.Equal(expected, PersonMissingBuilder.IsSelfAppearance(role, personName));
}
