using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// Availability via TMDB's <c>watch/providers</c> endpoint. This data is JustWatch-sourced but
/// officially licensed through TMDB, making it the safe default. Region link only (no per-provider
/// deep-links).
/// </summary>
public sealed class TmdbAvailabilitySource : IAvailabilitySource
{
    // Keep a served-stale entry around well past its freshness window so it can be served while a refresh
    // runs, and so it survives a quiet spell. A new fetch resets freshness; this just bounds memory.
    private static readonly TimeSpan _hardRetention = TimeSpan.FromDays(14);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly PluginLifetime _lifetime;
    private readonly ILogger<TmdbAvailabilitySource> _logger;

    // Keys with a background refresh in flight, so a burst of stale hits triggers one fetch, not many.
    private readonly ConcurrentDictionary<string, byte> _refreshing = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbAvailabilitySource"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="cache">The shared memory cache (so repeat lookups and rescans are cheap).</param>
    /// <param name="lifetime">The plugin lifetime, so a behind-the-scenes refresh stops on shutdown.</param>
    /// <param name="logger">The logger.</param>
    public TmdbAvailabilitySource(IHttpClientFactory httpClientFactory, IMemoryCache cache, PluginLifetime lifetime, ILogger<TmdbAvailabilitySource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "TMDB";

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.IncludeAvailability;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailabilityOffer>> GetOffersAsync(AvailabilityQuery query, CancellationToken cancellationToken)
    {
        if (!query.ProviderIds.TryGetValue("Tmdb", out var tmdbStr)
            || !int.TryParse(tmdbStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
        {
            return Array.Empty<AvailabilityOffer>();
        }

        var path = query.TargetKind == BaseItemKind.Series ? "tv" : "movie";

        // The watch/providers response carries every country, so cache it per title (independent of the
        // requested country) and let the mapper pick the country out. Makes rescans and the background
        // enrichment pass cheap, and avoids re-hitting TMDB for a title already looked up.
        var cacheKey = string.Create(CultureInfo.InvariantCulture, $"mtg:avail:{path}:{tmdbId}");
        var ttl = CacheTtl();

        // Serve a cached entry immediately. If it has gone stale, kick off a behind-the-scenes refresh
        // but still return the stale data now, so a lookup never blocks on the network for a known title.
        if (_cache.TryGetValue(cacheKey, out CachedWatch? cached) && cached is not null)
        {
            if (cached.FreshUntil <= DateTimeOffset.UtcNow)
            {
                RefreshInBackground(cacheKey, path, tmdbId, ttl);
            }

            return TmdbWatchMapper.Map(cached.Response, query.Country);
        }

        // Cache miss: nothing to serve, so fetch synchronously this once.
        TmdbWatchResponse? response;
        try
        {
            response = await FetchAsync(path, tmdbId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TMDB watch/providers for {Id} failed", tmdbId);
            return Array.Empty<AvailabilityOffer>();
        }

        Store(cacheKey, response, ttl);
        return TmdbWatchMapper.Map(response, query.Country);
    }

    private static TimeSpan CacheTtl()
    {
        var hours = Plugin.Instance?.Configuration.AvailabilityCacheHours ?? 24;
        return TimeSpan.FromHours(Math.Max(1, hours));
    }

    private void Store(string cacheKey, TmdbWatchResponse? response, TimeSpan ttl)
    {
        _cache.Set(
            cacheKey,
            new CachedWatch(response, DateTimeOffset.UtcNow.Add(ttl)),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _hardRetention });
    }

    private void RefreshInBackground(string cacheKey, string path, int tmdbId, TimeSpan ttl)
    {
        // One refresh per key at a time; the rest keep serving the stale entry until it lands.
        if (!_refreshing.TryAdd(cacheKey, 0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var fresh = await FetchAsync(path, tmdbId, _lifetime.Stopping).ConfigureAwait(false);
                Store(cacheKey, fresh, ttl);
            }
            catch (OperationCanceledException)
            {
                // Plugin shutting down; leave the stale entry in place.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background availability refresh for {Id} failed; keeping stale data", tmdbId);
            }
            finally
            {
                _refreshing.TryRemove(cacheKey, out _);
            }
        });
    }

    private async Task<TmdbWatchResponse?> FetchAsync(string path, int tmdbId, CancellationToken cancellationToken)
    {
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"https://api.themoviedb.org/3/{path}/{tmdbId}/watch/providers?api_key={TmdbClient.ResolveApiKey()}");

        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using var http = await HttpRetry.SendAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Get, url),
            _logger,
            ServiceNames.Tmdb,
            string.Create(CultureInfo.InvariantCulture, $"{path}/{tmdbId}/watch/providers"),
            cancellationToken).ConfigureAwait(false);
        if (!http.IsSuccessStatusCode)
        {
            _logger.LogWarning("TMDB watch/providers for {Id} returned {Status}", tmdbId, http.StatusCode);
            return null;
        }

        var stream = await http.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync<TmdbWatchResponse>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
        }
    }

    // A cached watch/providers response with the moment it stops being fresh. Past that the entry is still
    // served, but a lookup triggers a behind-the-scenes refresh.
    private sealed record CachedWatch(TmdbWatchResponse? Response, DateTimeOffset FreshUntil);
}
