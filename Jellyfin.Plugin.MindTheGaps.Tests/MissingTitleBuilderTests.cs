using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class MissingTitleBuilderTests
{
    private static GapItem Movie(string tmdbId = "603")
        => new()
        {
            Id = "filmography:movie:603",
            Name = "The Matrix",
            Year = 1999,
            TargetKind = BaseItemKind.Movie,
            ImageUrl = "https://image.tmdb.org/poster.jpg",
            ProviderIds = string.IsNullOrEmpty(tmdbId)
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = tmdbId }
        };

    [Fact]
    public void ToTitle_ShapesTheCardFromTheGap()
    {
        var title = MissingTitleBuilder.ToTitle(Movie(), "as Neo", null);

        Assert.NotNull(title);
        Assert.Equal("filmography:movie:603", title!.GapId);
        Assert.Equal("Movie", title.Kind);
        Assert.Equal("The Matrix", title.Title);
        Assert.Equal(1999, title.Year);
        Assert.Equal("as Neo", title.Role);
        Assert.Equal(603, title.TmdbId);
        Assert.Equal("https://image.tmdb.org/poster.jpg", title.ImageUrl);
    }

    [Fact]
    public void ToTitle_ReturnsNullWithoutAUsableTmdbId()
    {
        Assert.Null(MissingTitleBuilder.ToTitle(Movie(string.Empty), null, null));
        Assert.Null(MissingTitleBuilder.ToTitle(Movie("not-a-number"), null, null));
        Assert.Null(MissingTitleBuilder.ToTitle(Movie("0"), null, null));
    }

    [Fact]
    public void MergeRoles_JoinsTwoDistinctCredits()
        => Assert.Equal("as Neo · Director", MissingTitleBuilder.MergeRoles("as Neo", "Director"));

    [Fact]
    public void MergeRoles_SkipsARepeatedCredit()
        => Assert.Equal("as Neo", MissingTitleBuilder.MergeRoles("as Neo", "as Neo"));

    [Fact]
    public void MergeRoles_FallsBackToWhicheverSideHasSomething()
    {
        Assert.Equal("as Neo", MissingTitleBuilder.MergeRoles("as Neo", null));
        Assert.Equal("as Neo", MissingTitleBuilder.MergeRoles(null, "as Neo"));
    }

    [Fact]
    public void Because_NamesTheSeedAndAnyOthers()
    {
        var gap = new GapItem
        {
            SourceItemName = "Fargo",
            OtherSources = [new GapSourceRef { Name = "The Big Lebowski" }, new GapSourceRef { Name = "No Country for Old Men" }]
        };

        var because = MissingTitleBuilder.Because(gap);

        Assert.NotNull(because);
        Assert.Contains("Fargo", because, StringComparison.Ordinal);
        Assert.Contains("and 1 more", because, StringComparison.Ordinal);
    }

    [Fact]
    public void Because_ReadsFromTheListWhenTheGapCameFromAList()
    {
        var gap = new GapItem
        {
            SourceItemName = "  Best of 1995 ",
            SourceItemType = SourceItemTypes.TmdbList,
            OtherSources = [new GapSourceRef { Name = "Fargo" }]
        };

        Assert.Equal("From Best of 1995", MissingTitleBuilder.Because(gap));
    }

    [Fact]
    public void Because_KeepsTheSeedWordingForAnOwnedMovieOrSeries()
    {
        var movie = new GapItem { SourceItemName = "Fargo", SourceItemType = SourceItemTypes.Movie };
        var series = new GapItem { SourceItemName = "Fargo", SourceItemType = SourceItemTypes.Series };

        Assert.Equal("Because you have Fargo", MissingTitleBuilder.Because(movie));
        Assert.Equal("Because you have Fargo", MissingTitleBuilder.Because(series));
    }

    [Fact]
    public void Because_NullWhenTheGapNamesNoSeed()
        => Assert.Null(MissingTitleBuilder.Because(new GapItem()));

    [Fact]
    public void NewestFirst_OrdersByDateThenTitle_UndatedLast()
    {
        var older = new MissingTitle { Title = "B", ReleaseDate = new DateTime(2000, 1, 1) };
        var newer = new MissingTitle { Title = "A", ReleaseDate = new DateTime(2020, 1, 1) };
        var undated = new MissingTitle { Title = "C", ReleaseDate = null };

        var ordered = MissingTitleBuilder.NewestFirst([older, undated, newer]);

        Assert.Equal(["A", "B", "C"], ordered.Select(i => i.Title).ToArray());
    }
}
