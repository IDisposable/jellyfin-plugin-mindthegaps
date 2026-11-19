using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using TMDbLib.Objects.General.Schema;
using TMDbLib.Objects.Lists;
using TMDbLib.Objects.Search;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class TmdbClientTests
{
    [Fact]
    public void ResolveApiKey_NoPluginInstance_UsesDefault()
    {
        // Plugin.Instance is null outside a running server, so the public default applies.
        Assert.Equal(TmdbClient.DefaultApiKey, TmdbClient.ResolveApiKey());
    }

    [Fact]
    public void GetPosterUrl_BuildsAbsoluteUrl()
    {
        using var client = new TmdbClient(new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

        Assert.Equal("https://image.tmdb.org/t/p/w500/abc.jpg", client.GetPosterUrl("/abc.jpg"));
    }

    [Fact]
    public void GetPosterUrl_NullOrEmpty_ReturnsNull()
    {
        using var client = new TmdbClient(new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

        Assert.Null(client.GetPosterUrl(null));
        Assert.Null(client.GetPosterUrl(string.Empty));
    }

    [Fact]
    public void GetBackdropUrl_BuildsAbsoluteUrl()
    {
        using var client = new TmdbClient(new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

        Assert.Equal("https://image.tmdb.org/t/p/w1280/abc.jpg", client.GetBackdropUrl("/abc.jpg"));
    }

    [Fact]
    public void GetBackdropUrl_NullOrEmpty_ReturnsNull()
    {
        using var client = new TmdbClient(new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

        Assert.Null(client.GetBackdropUrl(null));
        Assert.Null(client.GetBackdropUrl(string.Empty));
    }

    [Fact]
    public async Task CollectListMovies_FetchesAllPagesAndFiltersNonMovies()
    {
        var firstPage = new GenericList
        {
            TotalPages = 3,
            Items = new List<TmdbEntity> { new SearchMovie { Id = 1 }, new SearchTv { Id = 2 } }
        };
        var pages = new List<int>();
        var result = await TmdbClient.CollectListMoviesAsync(
            firstPage,
            (page, _) =>
            {
                pages.Add(page);
                return Task.FromResult<GenericList?>(new GenericList
                {
                    Items = page == 2 ? [] : new List<TmdbEntity> { new SearchMovie { Id = 3 } }
                });
            },
            CancellationToken.None);

        Assert.Equal(new[] { 2, 3 }, pages);
        Assert.Collection(result, movie => Assert.Equal(1, movie.Id), movie => Assert.Equal(3, movie.Id));
    }

    [Fact]
    public async Task CollectListMovies_FailedLaterPageThrows()
    {
        var firstPage = new GenericList { TotalPages = 2, Items = new List<TmdbEntity> { new SearchMovie { Id = 1 } } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => TmdbClient.CollectListMoviesAsync(
            firstPage,
            (_, _) => Task.FromResult<GenericList?>(null),
            CancellationToken.None));
    }

    [Fact]
    public async Task CollectListMovies_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TmdbClient.CollectListMoviesAsync(
            new GenericList(),
            (_, _) => throw new InvalidOperationException(),
            cts.Token));
    }
}
