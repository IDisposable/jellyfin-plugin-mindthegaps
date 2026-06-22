using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;

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

    private readonly CachedApiClient _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvMazeClient"/> class.
    /// </summary>
    /// <param name="api">The cached API client.</param>
    public TvMazeClient(CachedApiClient api)
    {
        _api = api;
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

    // CachedApiClient caches the result and routes through the shared HttpRetry path.
    private Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
        => _api.GetJsonAsync<T>(ServiceNames.TvMaze, BaseUrl + path, CachedApiClient.DefaultCacheDuration, _jsonOptions, null, cancellationToken);
}
