using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
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
}
