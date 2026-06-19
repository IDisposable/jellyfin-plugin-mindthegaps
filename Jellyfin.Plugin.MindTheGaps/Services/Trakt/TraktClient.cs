using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// A minimal client for Trakt's public, read-only API. Requires only a user-supplied client id
/// (no OAuth). See https://trakt.docs.apiary.io/.
/// </summary>
public sealed class TraktClient
{
    private const string BaseUrl = "https://api.trakt.tv";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TraktClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraktClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public TraktClient(IHttpClientFactory httpClientFactory, ILogger<TraktClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a Trakt person id (slug preferred) from an external id.
    /// </summary>
    /// <param name="clientId">The Trakt client id.</param>
    /// <param name="idType">The external id type ("tmdb" or "imdb").</param>
    /// <param name="id">The external id value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A Trakt slug/id, or <see langword="null"/>.</returns>
    public async Task<string?> FindPersonTraktIdAsync(string clientId, string idType, string id, CancellationToken cancellationToken)
    {
        var results = await GetAsync<List<TraktSearchResult>>(
            clientId,
            string.Create(CultureInfo.InvariantCulture, $"/search/{idType}/{Uri.EscapeDataString(id)}?type=person"),
            cancellationToken).ConfigureAwait(false);

        var person = results?.FirstOrDefault(r => r.Person?.Ids is not null)?.Person;
        return person?.Ids?.Slug
            ?? person?.Ids?.Trakt?.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets a person's movie credits (cast + crew) from Trakt.
    /// </summary>
    /// <param name="clientId">The Trakt client id.</param>
    /// <param name="traktPersonId">The Trakt person slug/id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The credits, or <see langword="null"/>.</returns>
    public Task<TraktPersonMovieCredits?> GetPersonMovieCreditsAsync(string clientId, string traktPersonId, CancellationToken cancellationToken)
        => GetAsync<TraktPersonMovieCredits>(
            clientId,
            string.Create(CultureInfo.InvariantCulture, $"/people/{Uri.EscapeDataString(traktPersonId)}/movies?extended=full"),
            cancellationToken);

    private async Task<T?> GetAsync<T>(string clientId, string path, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var response = await HttpRetry.SendAsync(
                client,
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
                    request.Headers.Add("trakt-api-key", clientId);
                    request.Headers.Add("trakt-api-version", "2");
                    return request;
                },
                _logger,
                "Trakt",
                path,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Trakt GET {Path} returned {Status}", path, response.StatusCode);
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
            _logger.LogWarning(ex, "Trakt GET {Path} failed", path);
            return default;
        }
    }
}
