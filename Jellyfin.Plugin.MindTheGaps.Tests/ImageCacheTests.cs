using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Images;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Jellyfin.Plugin.MindTheGaps.Tests.FakeImageHost;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The cache's behaviour against a fake host: one fetch per image however often it is asked for, only allowed raster
// images kept, misses remembered, a host that keeps refusing us left alone for a while, nothing waiting on a queue or
// on a trim, and the oldest files trimmed past the cap.
public sealed class ImageCacheTests : IDisposable
{
    private const long Cap = 1024 * 1024;
    private static readonly Uri Poster = new("https://image.tmdb.org/t/p/w500/a.jpg");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mtg-images-" + Guid.NewGuid().ToString("N"));
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
    public async Task Get_FetchesOnceAndThenServesTheFile()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(200)));
        using var cache = Make(host);

        var first = await cache.GetAsync(Poster, Cap, default);
        var second = await cache.GetAsync(Poster, Cap, default);

        Assert.NotNull(first);
        Assert.Equal("image/jpeg", first.ContentType);
        Assert.Equal(first.Path, second!.Path);
        Assert.True(File.Exists(first.Path));
        Assert.StartsWith(_root, first.Path, StringComparison.Ordinal);
        Assert.Equal(1, host.Calls);
    }

    [Fact]
    public async Task Get_AsksTheProviderAsThePluginAndForwardsNothingOfTheBrowser()
    {
        HttpRequestMessage? sent = null;
        var host = new FakeImageHost(request =>
        {
            sent = request;
            return Ok(Jpeg(50));
        });
        using var cache = Make(host);

        await cache.GetAsync(Poster, Cap, default);

        Assert.NotNull(sent);
        Assert.Contains(sent.Headers.UserAgent, p => p.Product?.Name == "Jellyfin.Plugin.MindTheGaps");
        Assert.Equal("image/webp, image/*; q=0.8", sent.Headers.Accept.ToString());
        Assert.Null(sent.Headers.Authorization);
        Assert.False(sent.Headers.Contains("Cookie"));
        Assert.False(sent.Headers.Contains("Referer"));
    }

    [Fact]
    public async Task Get_NamesTheFileByTheAddress()
    {
        using var cache = Make(new FakeImageHost(_ => Ok(Jpeg(50))));

        var image = await cache.GetAsync(Poster, Cap, default);

        Assert.Equal(ImageCache.KeyFor(Poster), Path.GetFileName(image!.Path));
        Assert.NotEqual(ImageCache.KeyFor(Poster), ImageCache.KeyFor(new Uri("https://image.tmdb.org/t/p/w500/b.jpg")));
    }

    [Fact]
    public async Task Get_KeepsNothingThatIsNotARasterImage()
    {
        var host = new FakeImageHost(_ => Ok(System.Text.Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")));
        using var cache = Make(host);

        Assert.Null(await cache.GetAsync(Poster, Cap, default));
        Assert.False(File.Exists(Path.Combine(_root, ImageCache.KeyFor(Poster)[..2], ImageCache.KeyFor(Poster))));
    }

    [Fact]
    public async Task Get_RemembersAMissSoItIsNotAskedForAgainAtOnce()
    {
        var host = new FakeImageHost(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var cache = Make(host);

        Assert.Null(await cache.GetAsync(Poster, Cap, default));
        Assert.Null(await cache.GetAsync(Poster, Cap, default));

        Assert.Equal(1, host.Calls);
    }

    [Theory]
    [InlineData("no-store")]
    [InlineData("private, max-age=3600")]
    [InlineData("public, no-store")]
    public async Task Get_KeepsNothingTheProviderSaysNotToStore(string cacheControl)
    {
        var host = new FakeImageHost(_ =>
        {
            var response = Ok(Jpeg(100));
            response.Headers.CacheControl = System.Net.Http.Headers.CacheControlHeaderValue.Parse(cacheControl);
            return response;
        });
        using var cache = Make(host);

        Assert.Null(await cache.GetAsync(Poster, Cap, default));
        Assert.False(File.Exists(Path.Combine(_root, ImageCache.KeyFor(Poster)[..2], ImageCache.KeyFor(Poster))));
    }

    [Theory]
    [InlineData("public, max-age=31919000")]
    [InlineData("public")]
    [InlineData("no-cache")]
    public async Task Get_KeepsWhatTheProviderAllowsToBeShared(string cacheControl)
    {
        var host = new FakeImageHost(_ =>
        {
            var response = Ok(Jpeg(100));
            response.Headers.CacheControl = System.Net.Http.Headers.CacheControlHeaderValue.Parse(cacheControl);
            return response;
        });
        using var cache = Make(host);

        Assert.NotNull(await cache.GetAsync(Poster, Cap, default));
    }

    [Fact]
    public async Task Get_DoesNotRefreshAFilesAgeByServingIt()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(100)));
        using var cache = Make(host);
        var first = await cache.GetAsync(Poster, Cap, default);
        var fetched = DateTime.UtcNow.AddDays(-40);
        File.SetLastWriteTimeUtc(first!.Path, fetched);

        var served = await cache.GetAsync(Poster, Cap, default);

        Assert.Equal(fetched, File.GetLastWriteTimeUtc(first.Path));
        Assert.Equal(new DateTimeOffset(fetched, TimeSpan.Zero), served!.LastModified);
    }

    [Fact]
    public async Task Get_GivesAFileFetchedAgainANewETag()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(100)));
        using var cache = Make(host);
        var first = await cache.GetAsync(Poster, Cap, default);
        var again = await cache.GetAsync(Poster, Cap, default);
        Assert.Equal(first!.ETag, again!.ETag);

        // What the server's sweep and the next request amount to: the file is written afresh.
        File.SetLastWriteTimeUtc(first.Path, DateTime.UtcNow.AddDays(1));
        var refetched = await cache.GetAsync(Poster, Cap, default);

        Assert.NotEqual(first.ETag, refetched!.ETag);
        Assert.StartsWith("\"", refetched.ETag, StringComparison.Ordinal);
        Assert.EndsWith("\"", refetched.ETag, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_RefusesAnImageOverTheSizeLimit()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(ImageCache.MaxFileBytes + 1)));
        using var cache = Make(host);

        Assert.Null(await cache.GetAsync(Poster, Cap, default));
    }

    [Fact]
    public async Task Get_RefusesAnImageWhoseRedirectLeftTheAllowedHosts()
    {
        var host = new FakeImageHost(request =>
        {
            var response = Ok(Jpeg(100));
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/a.jpg");
            return response;
        });
        using var cache = Make(host);

        Assert.Null(await cache.GetAsync(Poster, Cap, default));
    }

    [Fact]
    public async Task Get_ShareOneFetchBetweenCallersAskingAtTheSameTime()
    {
        var release = new TaskCompletionSource();
        var host = new FakeImageHost(_ => Ok(Jpeg(100)), release.Task);
        using var cache = Make(host);

        var a = cache.GetAsync(Poster, Cap, default);
        var b = cache.GetAsync(Poster, Cap, default);
        release.SetResult();

        Assert.NotNull(await a);
        Assert.NotNull(await b);
        Assert.Equal(1, host.Calls);
    }

    [Fact]
    public async Task Get_SendsTheOverflowToTheProviderInsteadOfQueueingIt()
    {
        var release = new TaskCompletionSource();
        var host = new FakeImageHost(_ => Ok(Jpeg(100)), release.Task);
        using var cache = Make(host);

        var held = Enumerable.Range(1, 6).Select(i => cache.GetAsync(Address(i), Cap, default)).ToList();
        var overflow = await cache.GetAsync(Address(7), Cap, default);
        release.SetResult();
        var served = await Task.WhenAll(held);

        Assert.Null(overflow);
        Assert.All(served, image => Assert.NotNull(image));
        Assert.Equal(6, host.Calls);
    }

    [Fact]
    public async Task Get_StopsAskingAHostThatKeepsRefusingUsAndTriesAgainAfterTheCooldown()
    {
        var now = DateTimeOffset.UtcNow;
        var refusing = true;
        var host = new FakeImageHost(_ => refusing ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Ok(Jpeg(50)));
        using var cache = Make(host, new ImageHostHealth(() => now));

        for (var i = 1; i <= 5; i++)
        {
            Assert.Null(await cache.GetAsync(Address(i), Cap, default));
        }

        Assert.Equal(5, host.Calls);
        Assert.Null(await cache.GetAsync(Address(6), Cap, default));
        Assert.Equal(5, host.Calls);

        refusing = false;
        now += TimeSpan.FromMinutes(11);
        Assert.NotNull(await cache.GetAsync(Address(7), Cap, default));
        Assert.Equal(6, host.Calls);
    }

    [Fact]
    public async Task Get_DoesNotCountAMissingImageAgainstTheHost()
    {
        var host = new FakeImageHost(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var cache = Make(host);

        for (var i = 1; i <= 8; i++)
        {
            Assert.Null(await cache.GetAsync(Address(i), Cap, default));
        }

        Assert.Equal(8, host.Calls);
    }

    [Fact]
    public async Task Get_KeepsServingWhatIsHeldWhileTheHostIsBlocked()
    {
        var refusing = false;
        var host = new FakeImageHost(_ => refusing ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Ok(Jpeg(50)));
        using var cache = Make(host);
        Assert.NotNull(await cache.GetAsync(Poster, Cap, default));

        refusing = true;
        for (var i = 1; i <= 5; i++)
        {
            Assert.Null(await cache.GetAsync(Address(i), Cap, default));
        }

        Assert.NotNull(await cache.GetAsync(Poster, Cap, default));
    }

    [Fact]
    public async Task Get_FetchesNothingMoreOnceTheCacheIsPastTwiceItsCapUntilATrimBringsItDown()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(400)));
        using var cache = Make(host);
        await cache.GetAsync(Address(1), Cap, default);
        await cache.GetAsync(Address(2), Cap, default);

        // Nothing is known of the folder's size until a trim measures it: 800 bytes, which is under a cap of 1000
        // and so is left, but past twice a cap of 300.
        cache.Trim(1000, null, default);
        Assert.Null(await cache.GetAsync(Address(3), 300, default));
        Assert.Equal(2, host.Calls);

        cache.Trim(300, null, default);
        Assert.NotNull(await cache.GetAsync(Address(3), 300, default));
        Assert.Equal(3, host.Calls);
    }

    [Fact]
    public async Task Get_KeepsFetchingBeforeAnyTrimHasMeasuredTheFolder()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(400)));
        using var cache = Make(host);

        for (var i = 1; i <= 5; i++)
        {
            Assert.NotNull(await cache.GetAsync(Address(i), 100, default));
        }

        Assert.Equal(5, host.Calls);
    }

    [Fact]
    public async Task Trim_DeletesTheOldestFilesDownToNineTenthsOfTheCap()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(400)));
        using var cache = Make(host);
        var paths = new string[3];
        for (var i = 0; i < 3; i++)
        {
            paths[i] = (await cache.GetAsync(Address(i + 1), Cap, default))!.Path;
            File.SetLastWriteTimeUtc(paths[i], DateTime.UtcNow.AddDays(-3 + i));
        }

        var result = cache.Trim(1000, null, default);

        Assert.False(File.Exists(paths[0]));
        Assert.True(File.Exists(paths[1]));
        Assert.True(File.Exists(paths[2]));
        Assert.Equal(new TrimResult(3, 1200, 1, 800), result);
    }

    [Fact]
    public async Task Trim_ReportsProgressAsTheShareOfTheSpaceToFreeThatIsFreed()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(400)));
        using var cache = Make(host);
        for (var i = 0; i < 4; i++)
        {
            var image = await cache.GetAsync(Address(i + 1), Cap, default);
            File.SetLastWriteTimeUtc(image!.Path, DateTime.UtcNow.AddDays(-4 + i));
        }

        // 1600 bytes against a cap of 1000: down to 900 means freeing 700, which takes two files of 400.
        var reports = new System.Collections.Generic.List<double>();
        cache.Trim(1000, new InlineProgress(reports.Add), default);

        Assert.Equal(new[] { 400.0 / 700 * 100, 100.0 }, reports);
    }

    [Fact]
    public async Task Trim_LeavesACacheUnderItsCapAlone()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(400)));
        using var cache = Make(host);
        await cache.GetAsync(Address(1), Cap, default);

        var result = cache.Trim(Cap, null, default);

        Assert.Equal(new TrimResult(1, 400, 0, 400), result);
    }

    [Fact]
    public void Trim_OfAFolderThatDoesNotExistYetHasNothingToDo()
    {
        using var cache = Make(new FakeImageHost(_ => Ok(Jpeg(50))));

        Assert.Equal(new TrimResult(0, 0, 0, 0), cache.Trim(Cap, null, default));
    }

    [Fact]
    public void Trim_StopsWhenAskedTo()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(400)));
        using var cache = Make(host);
        var canceled = new CancellationToken(canceled: true);
        Directory.CreateDirectory(Path.Combine(_root, "ab"));
        File.WriteAllBytes(Path.Combine(_root, "ab", "file"), Jpeg(400));

        Assert.Throws<OperationCanceledException>(() => cache.Trim(100, null, canceled));
    }

    [Fact]
    public async Task Get_RemovesAStoredFileThatIsNotAnImage()
    {
        var host = new FakeImageHost(_ => Ok(Jpeg(100)));
        using var cache = Make(host);
        var path = Path.Combine(_root, ImageCache.KeyFor(Poster)[..2], ImageCache.KeyFor(Poster));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "not an image");

        var image = await cache.GetAsync(Poster, Cap, default);

        Assert.NotNull(image);
        Assert.Equal(1, host.Calls);
        Assert.Equal("image/jpeg", image.ContentType);
    }

    private static Uri Address(int n) => new("https://image.tmdb.org/t/p/w500/" + n + ".jpg");

    private ImageCache Make(FakeImageHost host, ImageHostHealth? health = null)
        => new(host, _root, new MemoryCache(new MemoryCacheOptions()), _lifetime, NullLogger<ImageCache>.Instance, health ?? new ImageHostHealth());

    // Reports on the calling thread and in order, which Progress<T> does not.
    private sealed class InlineProgress : IProgress<double>
    {
        private readonly Action<double> _report;

        public InlineProgress(Action<double> report) => _report = report;

        public void Report(double value) => _report(value);
    }
}
