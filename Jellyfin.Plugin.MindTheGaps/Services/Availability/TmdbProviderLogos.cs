using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// The logo of every streaming provider TMDB knows in a region, by provider name. A provider's logo is the same
/// on every title, so it is read once from TMDB's catalog (two small requests) instead of coming back with each
/// title's own lookup, which is what lets an offer stored without one be given its logo without looking the
/// title up again.
/// </summary>
public sealed class TmdbProviderLogos
{
    // A provider's logo barely ever changes, so a day is a long time to trust it.
    private static readonly TimeSpan _ttl = TimeSpan.FromHours(24);

    // A failed read is retried soon, but not on every call while TMDB is unreachable.
    private static readonly TimeSpan _failureTtl = TimeSpan.FromMinutes(10);

    private static readonly string[] _mediaTypes = ["movie", "tv"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TmdbProviderLogos> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbProviderLogos"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="cache">The shared memory cache.</param>
    /// <param name="logger">The logger.</param>
    public TmdbProviderLogos(IHttpClientFactory httpClientFactory, IMemoryCache cache, ILogger<TmdbProviderLogos> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Gets the logos for a region, from the cache when it holds them. An unreachable TMDB yields an empty map
    /// (and is retried soon), never an exception, since a missing logo only means an icon shows a letter.
    /// </summary>
    /// <param name="country">The ISO 3166-1 alpha-2 country code.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Logo URL by provider name, ignoring case.</returns>
    public async Task<IReadOnlyDictionary<string, string>> GetAsync(string country, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(country);

        var cacheKey = "mtg:provider-logos:" + country.ToUpperInvariant();
        if (_cache.TryGetValue(cacheKey, out IReadOnlyDictionary<string, string>? cached) && cached is not null)
        {
            return cached;
        }

        var catalogs = new List<TmdbProviderList?>(_mediaTypes.Length);
        var complete = true;
        foreach (var mediaType in _mediaTypes)
        {
            try
            {
                catalogs.Add(await FetchAsync(mediaType, country, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                complete = false;
                _logger.LogWarning(ex, "TMDB {Type} provider catalog for {Country} could not be read", mediaType, country);
            }
        }

        var map = BuildMap(catalogs);
        _cache.Set(cacheKey, map, complete ? _ttl : _failureTtl);
        return map;
    }

    /// <summary>
    /// Builds the name-to-logo map from provider catalogs. A provider TMDB has no logo for is left out, and the
    /// first catalog to list a name wins (the movie and series catalogs share almost every provider).
    /// </summary>
    /// <param name="catalogs">The catalogs, in order of preference.</param>
    /// <returns>Logo URL by provider name, ignoring case.</returns>
    internal static IReadOnlyDictionary<string, string> BuildMap(IEnumerable<TmdbProviderList?> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in catalogs)
        {
            foreach (var provider in catalog?.Results ?? [])
            {
                var url = TmdbClient.BuildLogoUrl(provider.LogoPath);
                if (!string.IsNullOrEmpty(provider.ProviderName) && url is not null)
                {
                    map.TryAdd(provider.ProviderName, url);
                }
            }
        }

        return map;
    }

    private async Task<TmdbProviderList?> FetchAsync(string mediaType, string country, CancellationToken cancellationToken)
    {
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"https://api.themoviedb.org/3/watch/providers/{mediaType}?watch_region={Uri.EscapeDataString(country)}&api_key={TmdbClient.ResolveApiKey()}");

        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using var http = await HttpRetry.SendAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Get, url),
            _logger,
            ServiceNames.Tmdb,
            "watch/providers/" + mediaType,
            cancellationToken).ConfigureAwait(false);
        if (!http.IsSuccessStatusCode)
        {
            _logger.LogWarning("TMDB {Type} provider catalog returned {Status}", mediaType, http.StatusCode);
            return null;
        }

        var stream = await http.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync<TmdbProviderList>(stream, TmdbWatchJson.Options, cancellationToken).ConfigureAwait(false);
        }
    }
}
