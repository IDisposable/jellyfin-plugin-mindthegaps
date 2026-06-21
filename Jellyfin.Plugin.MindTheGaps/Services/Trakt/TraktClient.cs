using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;

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

    private readonly CachedApiClient _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraktClient"/> class.
    /// </summary>
    /// <param name="api">The cached API client.</param>
    public TraktClient(CachedApiClient api)
    {
        _api = api;
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

    private Task<T?> GetAsync<T>(string clientId, string path, CancellationToken cancellationToken)
        where T : class
        => _api.GetJsonAsync<T>(
            "Trakt",
            BaseUrl + path,
            CachedApiClient.DefaultCacheDuration,
            _jsonOptions,
            request =>
            {
                request.Headers.Add("trakt-api-key", clientId);
                request.Headers.Add("trakt-api-version", "2");
            },
            cancellationToken);
}
