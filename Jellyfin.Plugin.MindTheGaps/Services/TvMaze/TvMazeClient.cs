using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.TvMaze;

/// <summary>
/// A minimal client for TVmaze's public, key-free REST API. See https://www.tvmaze.com/api.
/// </summary>
public sealed class TvMazeClient
{
    private const string BaseUrl = "https://api.tvmaze.com";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TvMazeClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvMazeClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public TvMazeClient(IHttpClientFactory httpClientFactory, ILogger<TvMazeClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a TVmaze show id from an external id.
    /// </summary>
    /// <param name="idType">The external id type ("thetvdb" or "imdb").</param>
    /// <param name="id">The external id value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The TVmaze show id, or <see langword="null"/>.</returns>
    public async Task<int?> ResolveShowIdAsync(string idType, string id, CancellationToken cancellationToken)
    {
        var show = await GetAsync<TvMazeShow>(
            string.Create(CultureInfo.InvariantCulture, $"/lookup/shows?{idType}={Uri.EscapeDataString(id)}"),
            cancellationToken).ConfigureAwait(false);
        return show?.Id;
    }

    /// <summary>
    /// Gets the regular (numbered) episodes of a TVmaze show.
    /// </summary>
    /// <param name="showId">The TVmaze show id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The episode list, or <see langword="null"/>.</returns>
    public Task<List<TvMazeEpisode>?> GetEpisodesAsync(int showId, CancellationToken cancellationToken)
        => GetAsync<List<TvMazeEpisode>>(
            string.Create(CultureInfo.InvariantCulture, $"/shows/{showId}/episodes"),
            cancellationToken);

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var response = await HttpRetry.SendAsync(
                client,
                () => new HttpRequestMessage(HttpMethod.Get, BaseUrl + path),
                _logger,
                "TVmaze",
                path,
                cancellationToken).ConfigureAwait(false);

            // A lookup miss is a normal 404, not an error worth logging.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TVmaze GET {Path} returned {Status}", path, response.StatusCode);
                return default;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TVmaze GET {Path} failed", path);
            return default;
        }
    }
}
