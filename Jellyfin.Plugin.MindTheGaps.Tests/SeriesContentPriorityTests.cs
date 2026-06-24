using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Jellyfin.Plugin.MindTheGaps.Services;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class SeriesContentPriorityTests
{
    // TheMovieDb above TheTVDB, as the resolved provider order from the library settings.
    private static readonly IReadOnlyList<KnownProvider?> TmdbThenTvdb = new KnownProvider?[] { KnownProviders.Tmdb, KnownProviders.Tvdb };

    [Fact]
    public void Rank_ListedProvider_IsItsPosition()
    {
        Assert.Equal(0, SeriesContentPriority.Rank(TmdbThenTvdb, KnownProviders.Tmdb));
        Assert.Equal(1, SeriesContentPriority.Rank(TmdbThenTvdb, KnownProviders.Tvdb));
    }

    [Fact]
    public void Rank_KnownProviderNotInOrder_RanksAfterTheListedOnes()
    {
        // IMDb is a known provider the library does not list as a series fetcher, so it ranks below the listed.
        Assert.Equal(TmdbThenTvdb.Count, SeriesContentPriority.Rank(TmdbThenTvdb, KnownProviders.Imdb));
    }

    [Fact]
    public void Rank_NonFetcherSource_RanksLast()
    {
        // TVmaze (a null provider) is not a Jellyfin metadata fetcher, so it always ranks last.
        Assert.Equal(int.MaxValue, SeriesContentPriority.Rank(TmdbThenTvdb, null));
    }

    [Fact]
    public void Rank_OrdersListedAboveUnlistedAboveNonFetcher()
    {
        Assert.True(SeriesContentPriority.Rank(TmdbThenTvdb, KnownProviders.Tmdb)
            < SeriesContentPriority.Rank(TmdbThenTvdb, KnownProviders.Imdb));
        Assert.True(SeriesContentPriority.Rank(TmdbThenTvdb, KnownProviders.Imdb)
            < SeriesContentPriority.Rank(TmdbThenTvdb, null));
    }

    [Fact]
    public void Rank_EmptyOrder_NonFetcherStillRanksLast()
    {
        // With no configured order a known provider ranks at the boundary; a non-fetcher is still last.
        Assert.Equal(0, SeriesContentPriority.Rank([], KnownProviders.Tmdb));
        Assert.Equal(int.MaxValue, SeriesContentPriority.Rank([], null));
    }
}
