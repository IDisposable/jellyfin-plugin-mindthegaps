using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class WatchlistSearchMapperTests
{
    private static OwnershipIndex Owns(BaseItemKind kind, params int[] tmdbIds)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in tmdbIds)
        {
            set.Add(OwnershipIndex.MakeKey(kind, "Tmdb", id.ToString(CultureInfo.InvariantCulture)));
        }

        return new OwnershipIndex(set);
    }

    private static OwnershipIndex Empty() => new(new HashSet<string>(StringComparer.Ordinal));

    [Fact]
    public void SearchMovies_SkipsOwned_AndUsesTheSearchPrefix_NotRecommendation()
    {
        var results = new[] { new SearchMovie { Id = 1, Title = "Owned" }, new SearchMovie { Id = 2, Title = "Wanted" } };

        var gaps = WatchlistSearchMapper.SearchMovies(results, Owns(BaseItemKind.Movie, 1), _ => null, limit: 20).ToList();

        var gap = Assert.Single(gaps);
        Assert.Equal("watchlistsearch:movie:2", gap.Id);
        Assert.Equal(GapPattern.Recommendation, gap.Pattern);
        Assert.Equal(MediaDomain.Movies, gap.Domain);
        Assert.Equal(string.Empty, gap.SourceItemId);
        Assert.Null(gap.SourceItemName);
    }

    [Fact]
    public void SearchSeries_SkipsOwned_AndUsesTheSearchPrefix()
    {
        var results = new[] { new SearchTv { Id = 5, Name = "Owned Show" }, new SearchTv { Id = 6, Name = "Wanted Show" } };

        var gaps = WatchlistSearchMapper.SearchSeries(results, Owns(BaseItemKind.Series, 5), _ => null, limit: 20).ToList();

        var gap = Assert.Single(gaps);
        Assert.Equal("watchlistsearch:series:6", gap.Id);
        Assert.Equal(MediaDomain.Shows, gap.Domain);
    }

    [Fact]
    public void FromMovieDetails_ReturnsGap_WhenUnowned()
    {
        var movie = new Movie { Id = 42, Title = "Wanted Movie", Overview = "A plot.", PosterPath = "/p.jpg" };

        var gap = WatchlistSearchMapper.FromMovieDetails(movie, Empty(), p => "poster:" + p);

        Assert.NotNull(gap);
        Assert.Equal("watchlistsearch:movie:42", gap!.Id);
        Assert.Equal("Wanted Movie", gap.Name);
        Assert.Equal("poster:/p.jpg", gap.ImageUrl);
        Assert.Equal("42", gap.ProviderIds["Tmdb"]);
    }

    [Fact]
    public void FromMovieDetails_ReturnsNull_WhenAlreadyOwned()
    {
        var movie = new Movie { Id = 42, Title = "Already Owned" };

        Assert.Null(WatchlistSearchMapper.FromMovieDetails(movie, Owns(BaseItemKind.Movie, 42), _ => null));
    }

    [Fact]
    public void FromSeriesDetails_ReturnsGap_WhenUnowned()
    {
        var show = new TvShow { Id = 7, Name = "Wanted Show" };

        var gap = WatchlistSearchMapper.FromSeriesDetails(show, Empty(), _ => null);

        Assert.NotNull(gap);
        Assert.Equal("watchlistsearch:series:7", gap!.Id);
        Assert.Equal(BaseItemKind.Series, gap.TargetKind);
    }

    [Fact]
    public void FromSeriesDetails_ReturnsNull_WhenAlreadyOwned()
        => Assert.Null(WatchlistSearchMapper.FromSeriesDetails(new TvShow { Id = 7, Name = "Owned" }, Owns(BaseItemKind.Series, 7), _ => null));
}
