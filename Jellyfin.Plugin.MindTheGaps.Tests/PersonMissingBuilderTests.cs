using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.PersonPage;
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
        return FilmographyGapMapper.Build(person, "person-id", "Robert Zemeckis", new OwnershipIndex(keys), p => p, 0, 0).ToList();
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
        Assert.True(movies.Count >= 50, $"expected the crew credits to survive, got {movies.Count}");

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
