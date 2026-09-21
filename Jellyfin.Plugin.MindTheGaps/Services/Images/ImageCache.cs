using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Images;

/// <summary>
/// Keeps the images the plugin's pages show (posters, cover art, service logos) on the server, so a browser
/// gets them from the server instead of from each provider. Files live under the server's cache path, named by
/// a hash of the address they came from, so a changed source is a new file and nothing needs invalidating.
/// The request path never waits on anything but the one fetch it asked for: an image that cannot be had at once
/// (not held, a host that has stopped answering, every fetch slot busy, the cache full) answers
/// <see langword="null"/> and the route sends the browser to the provider. Keeping the cache to its size is a
/// scheduled task's job (<see cref="Trim"/>), not the request's. The server's daily cache task deletes files not
/// written for thirty days and sets no size cap. A file's age is when it was fetched and serving it does not
/// change that, so the server's sweep is what refreshes an image: it deletes it a month after it was fetched and
/// the next request fetches it again, which bounds how long an image the provider has replaced stays stale.
/// </summary>
public sealed class ImageCache : IDisposable
{
    /// <summary>
    /// The largest image kept. A poster or a logo is a fraction of this; it stops a host from filling the disk
    /// with one response.
    /// </summary>
    internal const int MaxFileBytes = 5 * 1024 * 1024;

    // Between trims the cache can grow past its cap. Past this many times the cap, nothing more is fetched until
    // a trim brings it back down.
    private const int CeilingFactor = 2;
    private const int MaxParallelFetches = 6;
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MissTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StaleTempAfter = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _root;
    private readonly IMemoryCache _cache;
    private readonly PluginLifetime _lifetime;
    private readonly ImageHostHealth _health;
    private readonly ILogger<ImageCache> _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<CachedImage?>>> _inFlight = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(MaxParallelFetches);

    // What the folder holds, as far as is known: unknown (negative) until a trim has measured it, then that
    // measure plus what has been stored since. Only a guide for the ceiling; a trim replaces it with the truth.
    private long _bytes = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCache"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="applicationPaths">The server's paths, for its cache folder.</param>
    /// <param name="cache">The memory cache.</param>
    /// <param name="lifetime">The plugin lifetime.</param>
    /// <param name="logger">The logger.</param>
    public ImageCache(
        IHttpClientFactory httpClientFactory,
        IApplicationPaths applicationPaths,
        IMemoryCache cache,
        PluginLifetime lifetime,
        ILogger<ImageCache> logger)
        : this(httpClientFactory, Path.Combine(applicationPaths.CachePath, "mindthegaps", "images"), cache, lifetime, logger, new ImageHostHealth())
    {
    }

    internal ImageCache(
        IHttpClientFactory httpClientFactory,
        string root,
        IMemoryCache cache,
        PluginLifetime lifetime,
        ILogger<ImageCache> logger,
        ImageHostHealth health)
    {
        _httpClientFactory = httpClientFactory;
        _root = root;
        _cache = cache;
        _lifetime = lifetime;
        _logger = logger;
        _health = health;
    }

    /// <summary>
    /// Gets the name a file is stored under: a hash of the address.
    /// </summary>
    /// <param name="uri">The address the image comes from.</param>
    /// <returns>Sixty-four lowercase hexadecimal characters.</returns>
    internal static string KeyFor(Uri uri)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));

    /// <summary>
    /// Gets the most the cache may hold, from the configured megabytes.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>The cap in bytes.</returns>
    internal static long CapBytes(PluginConfiguration config)
        => Math.Max(1, config.ImageCacheMaxMegabytes) * 1024L * 1024L;

    /// <summary>
    /// Gets an image, fetching and keeping it first when it is not held and can be had at once.
    /// </summary>
    /// <param name="uri">The address the image comes from; the caller has already checked it against the allowed
    /// hosts.</param>
    /// <param name="capBytes">The most the cache may hold, which bounds how far it is let grow between trims.</param>
    /// <param name="cancellationToken">The cancellation token of the request asking. A fetch already under way
    /// for the image carries on for whoever else is waiting on it.</param>
    /// <returns>The image, or <see langword="null"/> when it could not be had: the host refused or has been
    /// failing, it is not a raster image, it is too large, the cache is full, or every fetch slot was busy. The
    /// caller sends the browser to <paramref name="uri"/> instead.</returns>
    public async Task<CachedImage?> GetAsync(Uri uri, long capBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var key = KeyFor(uri);
        var hit = Open(key);
        if (hit is not null)
        {
            return hit;
        }

        if (_health.IsBlocked(uri.IdnHost) || _cache.TryGetValue(MissKey(key), out _) || IsFull(capBytes))
        {
            return null;
        }

        var fetch = _inFlight.GetOrAdd(key, _ => new Lazy<Task<CachedImage?>>(() => FetchAsync(uri, key)));
        return await fetch.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the oldest images until the cache is at nine tenths of its cap, when it is over it, and takes the
    /// measure of the folder that the ceiling on growth is judged against. Runs on a scheduled task, never on a
    /// request.
    /// </summary>
    /// <param name="capBytes">The most the cache may hold.</param>
    /// <param name="progress">Receives the percentage of the space to be freed that has been freed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the folder held and what was deleted.</returns>
    internal TrimResult Trim(long capBytes, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var files = Scan().OrderBy(f => f.LastWriteTimeUtc).ToList();
        var before = files.Sum(f => f.Length);
        var total = before;
        var deleted = 0;
        if (before > capBytes)
        {
            var target = capBytes / 10 * 9;
            var toFree = before - target;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (total <= target)
                {
                    break;
                }

                try
                {
                    var length = file.Length;
                    file.Delete();
                    total -= length;
                    deleted++;
                    progress?.Report(100.0 * Math.Min(1.0, (double)(before - total) / toFree));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Image cache: could not delete {Path}", file.FullName);
                }
            }
        }

        Volatile.Write(ref _bytes, total);
        return new TrimResult(files.Count, before, deleted, total);
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private static string MissKey(string key) => "mtg-image-miss:" + key;

    // A refusal, as against a host that simply does not have the file.
    private static bool IsRefusal(HttpStatusCode status)
        => status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
            || (int)status >= 500;

    private bool IsFull(long capBytes)
    {
        var held = Volatile.Read(ref _bytes);
        return held >= 0 && held >= capBytes * CeilingFactor;
    }

    private string PathFor(string key) => Path.Combine(_root, key[..2], key);

    // The held image, or null. What is on disk is read for its first bytes, so a file that is not an image (a
    // truncated write, something else dropped in the folder) is never served, and is removed.
    private CachedImage? Open(string key)
    {
        var path = PathFor(key);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            Span<byte> head = stackalloc byte[ImageSniffer.HeadLength];
            int read;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                read = stream.Read(head);
            }

            var contentType = ImageSniffer.ContentType(head[..read]);
            if (contentType is null)
            {
                info.Delete();
                return null;
            }

            // The tag names the address and when the file was fetched, so an image fetched again after the
            // server's sweep is a new tag and a browser holding the old copy replaces it.
            var written = info.LastWriteTimeUtc;
            var etag = string.Create(CultureInfo.InvariantCulture, $"\"{key[..32]}-{written.Ticks:x}\"");
            return new CachedImage(path, contentType, etag, new DateTimeOffset(written, TimeSpan.Zero));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Image cache: could not read {Path}", path);
            return null;
        }
    }

    // A fetch takes a slot only if one is free: a page asking for thousands of images at once gets six fetched
    // and the rest sent to the provider, rather than a queue every request waits in.
    private async Task<CachedImage?> FetchAsync(Uri uri, string key)
    {
        var host = uri.IdnHost;
        try
        {
            if (!await _gate.WaitAsync(0).ConfigureAwait(false))
            {
                return null;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Stopping);
                timeout.CancelAfter(FetchTimeout);

                var download = await DownloadAsync(uri, timeout.Token).ConfigureAwait(false);
                if (download.HostFailed)
                {
                    Failed(host);
                }
                else if (_health.Answered(host))
                {
                    _logger.LogInformation("Image cache: {Host} is answering again", host);
                }

                if (download.Bytes is null)
                {
                    Miss(key);
                    return null;
                }

                Store(key, download.Bytes);
                return Open(key);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Image cache: could not fetch {Url}", uri);
            if (!_lifetime.Stopping.IsCancellationRequested)
            {
                Failed(host);
            }

            Miss(key);
            return null;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    // Said once per outage, not each time a cooldown ends and the trial request fails again.
    private void Failed(string host)
    {
        if (_health.Failed(host))
        {
            _logger.LogWarning("Image cache: {Host} has stopped answering, so browsers are sent to it directly until it does", host);
        }
    }

    // The image's bytes, or null when the host did not answer with an allowed raster image within the size
    // limit, and whether the host looks to have refused or dropped the server.
    private async Task<Download> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        // TMDB's image host answers by Accept (a browser's gets a WebP about half the size of the JPEG that
        // `image/*` gets), so ask for WebP first. Every browser jellyfin-web runs on renders it. AVIF is left out:
        // an older TV browser may not.
        request.Headers.Accept.ParseAdd("image/webp,image/*;q=0.8");

        // The plugin identifies itself, as it does to every API. Nothing of the browser's is forwarded: the
        // provider sees the server, not the viewer, and one file serves every viewer.
        HttpRetry.EnsureUserAgent(request);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new Download(null, IsRefusal(response.StatusCode));
        }

        // A provider that says an image is not to be stored is taken at its word. The host answered, so this
        // is not a failure of it.
        if (response.Headers.CacheControl is { NoStore: true } or { Private: true })
        {
            return new Download(null, false);
        }

        // A redirect is followed by the client, so where it ended is checked too.
        var final = response.RequestMessage?.RequestUri ?? uri;
        if (!ImageHosts.IsAllowed(final) || response.Content.Headers.ContentLength > MaxFileBytes)
        {
            return new Download(null, false);
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxFileBytes)
                {
                    return new Download(null, false);
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            var bytes = buffer.ToArray();
            return new Download(ImageSniffer.ContentType(bytes.AsSpan(0, Math.Min(bytes.Length, ImageSniffer.HeadLength))) is null ? null : bytes, false);
        }
    }

    private void Miss(string key) => _cache.Set(MissKey(key), true, MissTtl);

    // Written to a temporary name and moved into place, so a request never reads half a file.
    private void Store(string key, byte[] bytes)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
        if (Volatile.Read(ref _bytes) >= 0)
        {
            Interlocked.Add(ref _bytes, bytes.Length);
        }
    }

    // Every stored image, without a write still in progress: a temporary file counts once it has been left long
    // enough to be a failed one.
    private List<FileInfo> Scan()
    {
        var directory = new DirectoryInfo(_root);
        if (!directory.Exists)
        {
            return [];
        }

        return directory.EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !f.Name.EndsWith(".tmp", StringComparison.Ordinal) || DateTime.UtcNow - f.LastWriteTimeUtc > StaleTempAfter)
            .ToList();
    }

    private readonly record struct Download(byte[]? Bytes, bool HostFailed);
}
