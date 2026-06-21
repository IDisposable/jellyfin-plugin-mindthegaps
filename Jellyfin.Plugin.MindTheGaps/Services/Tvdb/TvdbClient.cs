using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// A minimal client for TheTVDB v4 REST API. Logs in with the user's own API key and caches the
/// bearer token, re-authenticating when it expires. See https://thetvdb.github.io/v4-api/.
/// </summary>
public sealed class TvdbClient : IDisposable
{
    private const string BaseUrl = "https://api4.thetvdb.com/v4";

    // Guards against runaway paging on a malformed response; real series fit comfortably.
    private const int MaxEpisodePages = 50;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CachedApiClient _api;
    private readonly ILogger<TvdbClient> _logger;
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    private string? _token;
    private string? _tokenApiKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvdbClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="api">The cached API client (a read-through cache around the token-authenticated fetch).</param>
    /// <param name="logger">The logger.</param>
    public TvdbClient(IHttpClientFactory httpClientFactory, CachedApiClient api, ILogger<TvdbClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _api = api;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a TheTVDB series id from an external (remote) id.
    /// </summary>
    /// <param name="apiKey">The user's TheTVDB API key.</param>
    /// <param name="remoteId">The external id value (e.g. an IMDb or TMDB id).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The TheTVDB series id, or <see langword="null"/>.</returns>
    public async Task<long?> ResolveSeriesIdAsync(string apiKey, string remoteId, CancellationToken cancellationToken)
    {
        var response = await GetAsync<TvdbRemoteIdResponse>(
            apiKey,
            string.Create(CultureInfo.InvariantCulture, $"/search/remoteid/{Uri.EscapeDataString(remoteId)}"),
            cancellationToken).ConfigureAwait(false);

        return response?.Data?
            .FirstOrDefault(r => r.Series is not null)?
            .Series?.Id;
    }

    /// <summary>
    /// Gets the full aired-order episode list for a series, across all pages.
    /// </summary>
    /// <param name="apiKey">The user's TheTVDB API key.</param>
    /// <param name="seriesId">The TheTVDB series id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The episodes, or <see langword="null"/> if the series could not be read.</returns>
    public async Task<IReadOnlyList<TvdbEpisode>?> GetEpisodesAsync(string apiKey, long seriesId, CancellationToken cancellationToken)
    {
        var episodes = new List<TvdbEpisode>();
        for (var page = 0; page < MaxEpisodePages; page++)
        {
            var response = await GetAsync<TvdbEpisodesResponse>(
                apiKey,
                string.Create(CultureInfo.InvariantCulture, $"/series/{seriesId}/episodes/default?page={page}"),
                cancellationToken).ConfigureAwait(false);

            var pageEpisodes = response?.Data?.Episodes;
            if (pageEpisodes is null || pageEpisodes.Count == 0)
            {
                return page == 0 && response is null ? null : episodes;
            }

            episodes.AddRange(pageEpisodes);

            if (string.IsNullOrEmpty(response?.Links?.Next))
            {
                break;
            }
        }

        return episodes;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _loginLock.Dispose();
    }

    // CachedApiClient caches the deserialized result by path (the same data regardless of which API key
    // fetched it); the fetch below carries the bearer-token login and its mid-scan re-auth.
    private Task<T?> GetAsync<T>(string apiKey, string path, CancellationToken cancellationToken)
        where T : class
        => _api.GetOrAddAsync<T>(
            "TheTVDB",
            path,
            CachedApiClient.DefaultCacheDuration,
            ct => FetchAsync<T>(apiKey, path, ct),
            cancellationToken);

    private async Task<T?> FetchAsync<T>(string apiKey, string path, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var token = await EnsureTokenAsync(apiKey, cancellationToken).ConfigureAwait(false);
            if (token is null)
            {
                return default;
            }

            var response = await SendAsync(token, path, cancellationToken).ConfigureAwait(false);

            // The token may have expired mid-scan; re-authenticate once and retry.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                token = await EnsureTokenAsync(apiKey, cancellationToken, forceRefresh: true).ConfigureAwait(false);
                if (token is null)
                {
                    return default;
                }

                response = await SendAsync(token, path, cancellationToken).ConfigureAwait(false);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return default;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("TheTVDB GET {Path} returned {Status}", path, response.StatusCode);
                    return default;
                }

                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TheTVDB GET {Path} failed", path);
            return default;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string token, string path, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        return await HttpRetry.SendAsync(
            client,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return request;
            },
            _logger,
            "TheTVDB",
            path,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> EnsureTokenAsync(string apiKey, CancellationToken cancellationToken, bool forceRefresh = false)
    {
        if (!forceRefresh && _token is not null && string.Equals(_tokenApiKey, apiKey, StringComparison.Ordinal))
        {
            return _token;
        }

        await _loginLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _token is not null && string.Equals(_tokenApiKey, apiKey, StringComparison.Ordinal))
            {
                return _token;
            }

            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var response = await HttpRetry.SendAsync(
                client,
                () => new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/login")
                {
                    Content = JsonContent.Create(new Dictionary<string, string> { ["apikey"] = apiKey })
                },
                _logger,
                "TheTVDB",
                "/login",
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TheTVDB login failed with {Status}; check the API key", response.StatusCode);
                _token = null;
                _tokenApiKey = null;
                return null;
            }

            var login = await response.Content.ReadFromJsonAsync<TvdbLoginResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            _token = login?.Data?.Token;
            _tokenApiKey = _token is null ? null : apiKey;
            return _token;
        }
        finally
        {
            _loginLock.Release();
        }
    }
}
