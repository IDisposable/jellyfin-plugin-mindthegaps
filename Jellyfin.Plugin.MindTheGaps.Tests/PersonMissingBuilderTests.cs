using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class PersonMissingBuilderTests
{
    private static GapItem Gap(string id, BaseItemKind kind, string? overview, int tmdbId = 1)
        => new()
        {
            Id = id,
            Name = "Some Title",
            TargetKind = kind,
            Overview = overview,
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = tmdbId.ToString(CultureInfo.InvariantCulture) }
        };

    [Theory]
    [InlineData("as self")]
    [InlineData("as himself")]
    [InlineData("as herself")]
    [InlineData("as Self - Host")]
    [InlineData("as Himself (archive footage)")]
    public void IsSelfAppearance_MatchesKnownSelfRoles(string role)
        => Assert.True(PersonMissingBuilder.IsSelfAppearance(role, "Someone Else"));

    [Fact]
    public void IsSelfAppearance_MatchesTheCreditedPersonsOwnName()
        => Assert.True(PersonMissingBuilder.IsSelfAppearance("as David Rappo", "David Rappo"));

    [Fact]
    public void IsSelfAppearance_MatchesTheCreditedPersonsOwnNameWithASuffix()
        => Assert.True(PersonMissingBuilder.IsSelfAppearance("as David Rappo - Cameo", "David Rappo"));

    [Fact]
    public void IsSelfAppearance_FalseWhenTheNameIsOnlyAPrefixOfARealRole()
        => Assert.False(PersonMissingBuilder.IsSelfAppearance("as David Rapport", "David Rappo"));

    [Fact]
    public void IsSelfAppearance_FalseForARealRole()
        => Assert.False(PersonMissingBuilder.IsSelfAppearance("as Marty McFly", "Michael J. Fox"));

    [Fact]
    public void IsSelfAppearance_FalseForNullOrEmptyRole()
    {
        Assert.False(PersonMissingBuilder.IsSelfAppearance(null, "Someone"));
        Assert.False(PersonMissingBuilder.IsSelfAppearance(string.Empty, "Someone"));
    }

    [Fact]
    public void Split_DropsDismissedGaps()
    {
        var gaps = new[] { Gap("a", BaseItemKind.Movie, "as Lead", tmdbId: 1) };
        var resolutions = new Dictionary<string, GapResolution> { ["a"] = new() };

        var (movies, series) = PersonMissingBuilder.Split(gaps, "Person", resolutions);

        Assert.Empty(movies);
        Assert.Empty(series);
    }

    [Fact]
    public void Split_DropsSelfAppearances()
    {
        var gaps = new[] { Gap("a", BaseItemKind.Movie, "as himself", tmdbId: 1) };

        var (movies, _) = PersonMissingBuilder.Split(gaps, "Person", new Dictionary<string, GapResolution>());

        Assert.Empty(movies);
    }

    [Fact]
    public void Split_MergesDuplicateCreditsOnSameTitle()
    {
        // The mapper emits one gap per credit; the same title directed and acted arrives twice under one id.
        var gaps = new[]
        {
            Gap("same-id", BaseItemKind.Movie, "as Lead", tmdbId: 1),
            Gap("same-id", BaseItemKind.Movie, "Director", tmdbId: 1)
        };

        var (movies, _) = PersonMissingBuilder.Split(gaps, "Person", new Dictionary<string, GapResolution>());

        var only = Assert.Single(movies);
        Assert.Contains("as Lead", only.Role, StringComparison.Ordinal);
        Assert.Contains("Director", only.Role, StringComparison.Ordinal);
    }

    [Fact]
    public void Split_SeparatesMoviesAndSeries()
    {
        var gaps = new[]
        {
            Gap("m", BaseItemKind.Movie, "as Lead", tmdbId: 1),
            Gap("s", BaseItemKind.Series, "as Regular", tmdbId: 2)
        };

        var (movies, series) = PersonMissingBuilder.Split(gaps, "Person", new Dictionary<string, GapResolution>());

        Assert.Single(movies);
        Assert.Single(series);
    }

    [Fact]
    public void Split_SkipsAGapWithNoUsableTmdbId()
    {
        var gap = new GapItem { Id = "a", Name = "No Tmdb Id", TargetKind = BaseItemKind.Movie, Overview = "as Lead" };

        var (movies, _) = PersonMissingBuilder.Split([gap], "Person", new Dictionary<string, GapResolution>());

        Assert.Empty(movies);
    }
}
