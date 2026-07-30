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
internal sealed class TraktClient
{
    private const string BaseUrl = "https://api.trakt.tv";

    // Trakt's page size for the watchlist and list walks. A large list is a handful of pages.
    private const int WatchlistPageSize = 100;

    // Bounds the list walk. Well above any list the discovery source will emit from, so it is a runaway
    // guard rather than a cap the user meets.
    private const int MaxListItems = 5000;

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

    private static string? ClientId => Plugin.Instance?.Configuration.TraktClientId;

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

    /// <summary>
    /// Gets a list's items (movies and shows merged), each carrying the external ids Trakt records. Empty
    /// when no client id is configured or the list is empty.
    /// </summary>
    /// <param name="listId">The Trakt list id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list's items.</returns>
    public async Task<IReadOnlyList<TraktListItem>> GetListItemsAsync(string listId, CancellationToken cancellationToken)
    {
        var clientId = ClientId;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(listId))
        {
            return [];
        }

        // Trakt paginates this endpoint whether or not you ask: without a page parameter it answers 100
        // items and says nothing about the rest, so a 498-item list read as one call silently became its
        // first 100. The pages are followed to MaxListItems.
        var items = new List<TraktListItem>();
        var list = Uri.EscapeDataString(listId);

        for (var page = 1; items.Count < MaxListItems; page++)
        {
            var pageItems = await GetAsync<List<TraktListItem>>(
                clientId,
                string.Create(CultureInfo.InvariantCulture, $"/lists/{list}/items/movie,show?extended=full&page={page}&limit={WatchlistPageSize}"),
                cancellationToken).ConfigureAwait(false);
            if (pageItems is null || pageItems.Count == 0)
            {
                break;
            }

            items.AddRange(pageItems);
            if (pageItems.Count < WatchlistPageSize)
            {
                break;
            }
        }

        return items;
    }

    /// <summary>
    /// Reads a user's watchlist (movies and shows merged), following the pages until it ends or
    /// <paramref name="maxItems"/> is reached. Needs only the client id: Trakt serves a public profile's
    /// watchlist without OAuth. Returns null when no client id is set.
    /// </summary>
    /// <remarks>
    /// Trakt gives no way to tell an empty watchlist from a private profile or a misspelled username: all
    /// three answer 200 with an empty array, and the documented X-Private-User header is not sent. A caller
    /// can therefore only report what it read, not why it read nothing.
    /// </remarks>
    /// <param name="username">The Trakt username or slug.</param>
    /// <param name="maxItems">The most entries to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The watchlist entries, or null when unconfigured.</returns>
    public async Task<IReadOnlyList<TraktListItem>?> GetWatchlistAsync(
        string username,
        int maxItems,
        CancellationToken cancellationToken)
    {
        var clientId = ClientId;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var user = Uri.EscapeDataString(username.Trim());
        var items = new List<TraktListItem>();

        for (var page = 1; items.Count < maxItems; page++)
        {
            var pageItems = await GetAsync<List<TraktListItem>>(
                clientId,
                string.Create(CultureInfo.InvariantCulture, $"/users/{user}/watchlist/movies,shows?page={page}&limit={WatchlistPageSize}"),
                cancellationToken).ConfigureAwait(false);
            if (pageItems is null)
            {
                return items.Count > 0 ? items : null;
            }

            items.AddRange(pageItems);
            if (pageItems.Count < WatchlistPageSize)
            {
                break;
            }
        }

        return items;
    }

    /// <summary>
    /// Resolves a list id to its display name (for the chip), or null when not found or no client id is set.
    /// </summary>
    /// <param name="listId">The Trakt list id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list name, or null.</returns>
    public async Task<string?> GetListNameAsync(string listId, CancellationToken cancellationToken)
    {
        var clientId = ClientId;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(listId))
        {
            return null;
        }

        var list = await GetAsync<TraktList>(
            clientId,
            string.Create(CultureInfo.InvariantCulture, $"/lists/{Uri.EscapeDataString(listId)}"),
            cancellationToken).ConfigureAwait(false);
        return list?.Name;
    }

    private Task<T?> GetAsync<T>(string clientId, string path, CancellationToken cancellationToken)
        where T : class
        => _api.GetJsonAsync<T>(
            ServiceNames.Trakt,
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
