using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Http;

/// <summary>
/// The shared, cached GET path for the plugin's hand-rolled API clients (Trakt, TVmaze, TheTVDB, MusicBrainz,
/// OpenLibrary, Discogs). It puts a read-through memory cache in front of every external call, so a repeated
/// lookup (within a scan or across back-to-back scans) does not hit the network again: the plugin stays a
/// good API citizen and well clear of rate-limit bans. A miss flows through <see cref="HttpRetry"/> (its
/// per-service pacing, retry/backoff, and circuit breaker), then the JSON is deserialized and the result
/// cached. Only a successful, non-empty result is cached, so a transient failure recovers on the next pass
/// rather than being remembered. TMDB is not here: it goes through TMDbLib, which carries its own cache and
/// retry.
/// </summary>
internal sealed class CachedApiClient
{
    /// <summary>
    /// The default time a fetched result is cached: long enough to cover a scan and any quick re-scan, short
    /// enough that the next day's scan re-reads catalog data that may have changed.
    /// </summary>
    public static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromHours(12);

    /// <summary>
    /// The cache time for a stable id-to-name (text) lookup: a studio, keyword, or label name effectively
    /// never changes, so there is no reason to re-fetch it on the next scan. Held much longer than
    /// <see cref="DefaultCacheDuration"/> (in memory, so in practice until the server restarts or this elapses).
    /// </summary>
    public static readonly TimeSpan StableCacheDuration = TimeSpan.FromDays(30);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachedApiClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedApiClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="cache">The shared memory cache.</param>
    /// <param name="logger">The logger.</param>
    public CachedApiClient(IHttpClientFactory httpClientFactory, IMemoryCache cache, ILogger<CachedApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Read-through cache primitive: returns the cached value for the key, or runs <paramref name="fetch"/>
    /// and caches a non-null result. Used directly by a client whose request needs more than a plain GET (the
    /// TheTVDB token flow); the JSON GET below is built on it.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="service">The service name, for the cache namespace (the same one passed to <see cref="HttpRetry"/>).</param>
    /// <param name="cacheKey">A key unique to this request within the service (usually the URL or path).</param>
    /// <param name="cacheDuration">How long to cache a non-null result.</param>
    /// <param name="fetch">Fetches the value on a cache miss; returns null on failure or absence (not cached).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cached or freshly fetched value, or null.</returns>
    public async Task<T?> GetOrAddAsync<T>(
        string service,
        string cacheKey,
        TimeSpan cacheDuration,
        Func<CancellationToken, Task<T?>> fetch,
        CancellationToken cancellationToken)
        where T : class
    {
        var key = string.Concat("mtgapi:", service, ":", cacheKey);
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var result = await fetch(cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            _cache.Set(key, result, cacheDuration);
        }

        return result;
    }

    /// <summary>
    /// GETs a URL and deserializes the JSON body, behind the read-through cache. The request is built fresh
    /// and configured (auth headers) by <paramref name="configureRequest"/>, and the call flows through
    /// <see cref="HttpRetry"/>. Returns null on a 404, a non-success status, or a transient failure (none of
    /// which are cached).
    /// </summary>
    /// <typeparam name="T">The response type to deserialize.</typeparam>
    /// <param name="service">The service name (for pacing, the circuit, logs, and the cache namespace).</param>
    /// <param name="url">The absolute request URL (also the cache key).</param>
    /// <param name="cacheDuration">How long to cache a successful result.</param>
    /// <param name="options">The JSON options to deserialize with.</param>
    /// <param name="configureRequest">Configures the request (for example auth headers), or null.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The deserialized response, or null.</returns>
    public Task<T?> GetJsonAsync<T>(
        string service,
        string url,
        TimeSpan cacheDuration,
        JsonSerializerOptions options,
        Action<HttpRequestMessage>? configureRequest,
        CancellationToken cancellationToken)
        where T : class
        => GetOrAddAsync(
            service,
            url,
            cacheDuration,
            ct => FetchJsonAsync<T>(service, url, options, configureRequest, ct),
            cancellationToken);

    private async Task<T?> FetchJsonAsync<T>(
        string service,
        string url,
        JsonSerializerOptions options,
        Action<HttpRequestMessage>? configureRequest,
        CancellationToken cancellationToken)
        where T : class
    {
        // The URL is the real request and cache key; what gets logged is the redacted form (a key in the
        // query string, like MDBList's apikey, would otherwise land in the log).
        var logUrl = LogSafe.Redact(url);
        try
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var response = await HttpRetry.SendAsync(
                client,
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    configureRequest?.Invoke(request);
                    return request;
                },
                _logger,
                service,
                logUrl,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Service} GET {Url} returned {Status}", service, logUrl, response.StatusCode);
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Service} GET {Url} failed", service, logUrl);
            return null;
        }
    }
}
