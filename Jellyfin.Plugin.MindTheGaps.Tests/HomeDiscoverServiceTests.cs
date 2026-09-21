using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class HomeDiscoverServiceTests
{
    private static GapItem Rec(string id, string name, int tmdbId, double? sortScore = null, string? sourceItemId = null, int otherSources = 0, bool adhoc = false, BaseItemKind kind = BaseItemKind.Movie, string sourceItemType = SourceItemTypes.Movie)
    {
        var gap = new GapItem
        {
            Id = id,
            Name = name,
            Pattern = GapPattern.Recommendation,
            TargetKind = kind,
            SortScore = sortScore,
            SourceItemId = sourceItemId,
            SourceItemType = sourceItemType,
            Adhoc = adhoc,
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = tmdbId.ToString(CultureInfo.InvariantCulture) }
        };
        if (otherSources > 0)
        {
            var others = new List<GapSourceRef>();
            for (var i = 0; i < otherSources; i++)
            {
                others.Add(new GapSourceRef { Name = "Other " + i });
            }

            gap.OtherSources = others;
        }

        return gap;
    }

    [Fact]
    public void Rank_ExcludesNonRecommendationGaps()
    {
        var setCompletion = new GapItem { Id = "a", Pattern = GapPattern.SetCompletion, TargetKind = BaseItemKind.Movie, ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = "1" } };

        var ranked = HomeDiscoverService.Rank([setCompletion], new Dictionary<string, GapResolution>(), 10);

        Assert.Empty(ranked);
    }

    [Fact]
    public void Rank_ExcludesAdhocGaps()
    {
        var gap = Rec("a", "A", 1, adhoc: true);

        var ranked = HomeDiscoverService.Rank([gap], new Dictionary<string, GapResolution>(), 10);

        Assert.Empty(ranked);
    }

    [Fact]
    public void Rank_ExcludesDismissedGaps()
    {
        var gap = Rec("a", "A", 1);
        var resolutions = new Dictionary<string, GapResolution> { ["a"] = new() };

        var ranked = HomeDiscoverService.Rank([gap], resolutions, 10);

        Assert.Empty(ranked);
    }

    [Fact]
    public void Rank_ExcludesGapsWhoseSeedWasMuted()
    {
        var seedId = Guid.NewGuid().ToString("N");
        var gap = Rec("a", "A", 1, sourceItemId: seedId);
        var resolutions = new Dictionary<string, GapResolution> { [GapResolution.RecSourcePrefix + seedId] = new() };

        var ranked = HomeDiscoverService.Rank([gap], resolutions, 10);

        Assert.Empty(ranked);
    }

    [Fact]
    public void Rank_OrdersByAgreementCountThenPopularityThenName()
    {
        var agreedByTwo = Rec("a", "Agreed By Two", 1, sortScore: 1, otherSources: 1);
        var popular = Rec("b", "Popular", 2, sortScore: 100);
        var lessPopular = Rec("c", "Less Popular", 3, sortScore: 10);

        var ranked = HomeDiscoverService.Rank([lessPopular, popular, agreedByTwo], new Dictionary<string, GapResolution>(), 10);

        Assert.Equal(["Agreed By Two", "Popular", "Less Popular"], [ranked[0].Title, ranked[1].Title, ranked[2].Title]);
    }

    [Fact]
    public void Rank_RespectsTheLimit()
    {
        var gaps = new[] { Rec("a", "A", 1), Rec("b", "B", 2), Rec("c", "C", 3) };

        var ranked = HomeDiscoverService.Rank(gaps, new Dictionary<string, GapResolution>(), 2);

        Assert.Equal(2, ranked.Count);
    }

    [Fact]
    public void Rank_IncludesTheBecauseLabel()
    {
        var gap = Rec("a", "A", 1);
        gap.SourceItemName = "Seed Movie";

        var ranked = HomeDiscoverService.Rank([gap], new Dictionary<string, GapResolution>(), 10);

        Assert.Contains("Seed Movie", ranked[0].Because, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SourceItemTypes.TmdbAccountList)]
    [InlineData(SourceItemTypes.TraktWatchlist)]
    [InlineData(SourceItemTypes.MdbListWatchlist)]
    [InlineData(SourceItemTypes.JustWatchList)]
    [InlineData(SourceItemTypes.ImdbList)]
    [InlineData(SourceItemTypes.TvdbFavorites)]
    [InlineData(SourceItemTypes.TmdbList)]
    [InlineData(SourceItemTypes.MdbList)]
    [InlineData(SourceItemTypes.TraktList)]
    [InlineData(SourceItemTypes.TmdbMovieDiscover)]
    public void Rank_LeavesOutATitleThatCameFromAList(string listKind)
    {
        var fromList = Rec("list", "From A List", 1, sourceItemType: listKind);
        var fromOwned = Rec("owned", "From An Owned Title", 2);

        var ranked = HomeDiscoverService.Rank([fromList, fromOwned], new Dictionary<string, GapResolution>(), 10);

        Assert.Equal("owned", Assert.Single(ranked).GapId);
    }

    [Fact]
    public void Rank_KeepsATitleSuggestedByAnOwnedSeries()
    {
        var gap = Rec("s", "A Series", 1, kind: BaseItemKind.Series, sourceItemType: SourceItemTypes.Series);

        var ranked = HomeDiscoverService.Rank([gap], new Dictionary<string, GapResolution>(), 10);

        Assert.Equal("s", Assert.Single(ranked).GapId);
    }

    [Fact]
    public void Rank_LeavesOutAGapWithNoSourceType()
    {
        var gap = Rec("a", "A", 1);
        gap.SourceItemType = null;

        Assert.Empty(HomeDiscoverService.Rank([gap], new Dictionary<string, GapResolution>(), 10));
    }

    [Fact]
    public void IsFromOwnedTitle_IsTrueOnlyForAnOwnedMovieOrSeries()
    {
        Assert.True(HomeDiscoverService.IsFromOwnedTitle(Rec("a", "A", 1, sourceItemType: SourceItemTypes.Movie)));
        Assert.True(HomeDiscoverService.IsFromOwnedTitle(Rec("b", "B", 2, sourceItemType: SourceItemTypes.Series)));
        Assert.False(HomeDiscoverService.IsFromOwnedTitle(Rec("c", "C", 3, sourceItemType: SourceItemTypes.TraktWatchlist)));
        Assert.Throws<ArgumentNullException>(() => HomeDiscoverService.IsFromOwnedTitle(null!));
    }
}
