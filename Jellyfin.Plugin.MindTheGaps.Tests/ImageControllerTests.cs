using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Api;
using Jellyfin.Plugin.MindTheGaps.Services.Images;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Jellyfin.Plugin.MindTheGaps.Tests.FakeImageHost;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// What the image route answers: the image when it has it, and otherwise a temporary redirect to the provider, so a
// host that refuses the server still shows its images to a browser that can reach it.
public sealed class ImageControllerTests : IDisposable
{
    private const long Cap = 1024 * 1024;
    private static readonly Uri Poster = new("https://image.tmdb.org/t/p/w500/a.jpg");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mtg-image-route-" + Guid.NewGuid().ToString("N"));
    private readonly PluginLifetime _lifetime = new();

    public void Dispose()
    {
        _lifetime.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task ServesAHeldImageWithLongCacheHeaders()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(200)));
        var (controller, cache) = Make(host);
        using var _ = cache;

        var result = await controller.Serve(Poster, true, Cap, default);

        var file = Assert.IsType<FileStreamResult>(result);
        await using var __ = file.FileStream;
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Contains("max-age=604800", controller.Response.Headers.CacheControl.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task SendsTheBrowserToTheProviderWhenTheFetchFails(HttpStatusCode status)
    {
        var host = new FakeImageHost(_ => new HttpResponseMessage(status));
        var (controller, cache) = Make(host);
        using var _ = cache;

        var result = await controller.Serve(Poster, true, Cap, default);

        AssertTemporaryRedirectTo(Poster, result);
        Assert.Contains("max-age=60", controller.Response.Headers.CacheControl.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendsTheBrowserToTheProviderWhenTheCacheIsOffAndFetchesNothing()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(200)));
        var (controller, cache) = Make(host);
        using var _ = cache;

        var result = await controller.Serve(Poster, false, Cap, default);

        AssertTemporaryRedirectTo(Poster, result);
        Assert.Equal(0, host.Calls);
    }

    [Fact]
    public async Task SendsTheBrowserToTheProviderOnceAHostIsBlocked()
    {
        var host = new FakeImageHost(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var (controller, cache) = Make(host);
        using var _ = cache;
        for (var i = 1; i <= 5; i++)
        {
            await controller.Serve(new Uri("https://image.tmdb.org/t/p/w500/" + i + ".jpg"), true, Cap, default);
        }

        var result = await controller.Serve(new Uri("https://image.tmdb.org/t/p/w500/6.jpg"), true, Cap, default);

        AssertTemporaryRedirectTo(new Uri("https://image.tmdb.org/t/p/w500/6.jpg"), result);
        Assert.Equal(5, host.Calls);
    }

    private static void AssertTemporaryRedirectTo(Uri expected, IActionResult result)
    {
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.False(redirect.Permanent);
        Assert.Equal(expected.AbsoluteUri, redirect.Url);
    }

    private (ImageController Controller, ImageCache Cache) Make(FakeImageHost host)
    {
        var cache = new ImageCache(host, _root, new MemoryCache(new MemoryCacheOptions()), _lifetime, NullLogger<ImageCache>.Instance, new ImageHostHealth());
        var controller = new ImageController(cache)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, cache);
    }
}
