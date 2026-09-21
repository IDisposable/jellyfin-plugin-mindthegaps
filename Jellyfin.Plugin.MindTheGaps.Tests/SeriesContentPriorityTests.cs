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

    [Fact]
    public void Uses_ListedProvider_IsUsed()
    {
        Assert.True(SeriesContentPriority.Uses(TmdbThenTvdb, KnownProviders.Tmdb));
        Assert.True(SeriesContentPriority.Uses(TmdbThenTvdb, KnownProviders.Tvdb));
    }

    [Fact]
    public void Uses_KnownProviderNotInOrder_IsNotUsed()
    {
        // The library configured an order and chose not to include IMDb, so it is excluded on purpose.
        Assert.False(SeriesContentPriority.Uses(TmdbThenTvdb, KnownProviders.Imdb));
    }

    [Fact]
    public void Uses_EmptyOrder_EveryCredentialedProviderIsUsed()
    {
        // A library that never configured its fetchers is still cross-checked rather than getting nothing.
        Assert.True(SeriesContentPriority.Uses([], KnownProviders.Tmdb));
        Assert.True(SeriesContentPriority.Uses([], KnownProviders.Imdb));
        Assert.True(SeriesContentPriority.Uses([], null));
    }

    [Fact]
    public void Uses_NonFetcherSource_IsAlwaysUsed()
    {
        // TVmaze (a null provider) is not a Jellyfin metadata fetcher, so a library's fetcher order can never
        // name it - it must not read as "excluded" just because a configured order exists. This is the
        // regression this test guards: before the fix, TvMazeEpisodeProvider reported a real KnownProvider
        // that could never appear in an order, so it was silently dropped from the cross-check on any library
        // with a configured fetcher order (which is most of them).
        Assert.True(SeriesContentPriority.Uses(TmdbThenTvdb, null));
    }
}
