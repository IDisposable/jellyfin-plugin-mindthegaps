using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;

namespace Jellyfin.Plugin.MindTheGaps.Services.MdbList;

/// <summary>
/// A minimal client for the MDBList HTTP API (https://api.mdblist.com). MDBList authenticates with an
/// <c>apikey</c> query parameter (a free key allows about 1000 requests a day), so a key is required and
/// comes from the plugin configuration. Calls go through the shared read-through cache, so a list's items
/// are not re-fetched within or across back-to-back scans. See https://docs.mdblist.com.
/// </summary>
public sealed class MdbListClient
{
    private const string BaseUrl = "https://api.mdblist.com";
    private const int MaxSuggestions = 10;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CachedApiClient _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="MdbListClient"/> class.
    /// </summary>
    /// <param name="api">The cached API client.</param>
    public MdbListClient(CachedApiClient api)
    {
        _api = api;
    }

    private static string? ApiKey => Plugin.Instance?.Configuration.MdbListApiKey;

    /// <summary>
    /// Searches MDBList's public lists by title for the settings type-ahead. Empty when the query or the
    /// configured key is blank (MDBList search is authenticated).
    /// </summary>
    /// <param name="query">The partial list title typed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching lists (id and name), capped for the dropdown.</returns>
    public async Task<IReadOnlyList<CuratedSetRef>> SearchListsAsync(string query, CancellationToken cancellationToken)
    {
        var key = ApiKey;
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(key))
        {
            return Array.Empty<CuratedSetRef>();
        }

        var url = string.Create(CultureInfo.InvariantCulture, $"{BaseUrl}/lists/search?query={Uri.EscapeDataString(query)}&apikey={key}");
        var lists = await _api.GetJsonAsync<List<MdbListListDto>>(ServiceNames.MdbList, url, CachedApiClient.DefaultCacheDuration, _jsonOptions, null, cancellationToken).ConfigureAwait(false);
        if (lists is null)
        {
            return Array.Empty<CuratedSetRef>();
        }

        var refs = new List<CuratedSetRef>();
        foreach (var list in lists)
        {
            if (list.Id > 0 && !string.IsNullOrEmpty(list.Name))
            {
                refs.Add(new CuratedSetRef { Id = list.Id, Name = list.Name });
            }

            if (refs.Count >= MaxSuggestions)
            {
                break;
            }
        }

        return refs;
    }

    /// <summary>
    /// Resolves a list id to its display name (for the chip), or null when not found or no key is set.
    /// </summary>
    /// <param name="listId">The MDBList list id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list name, or null.</returns>
    public async Task<string?> GetListNameAsync(int listId, CancellationToken cancellationToken)
    {
        var key = ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var url = string.Create(CultureInfo.InvariantCulture, $"{BaseUrl}/lists/{listId}?apikey={key}");
        var lists = await _api.GetJsonAsync<List<MdbListListDto>>(ServiceNames.MdbList, url, CachedApiClient.StableCacheDuration, _jsonOptions, null, cancellationToken).ConfigureAwait(false);
        return lists is { Count: > 0 } ? lists[0].Name : null;
    }

    /// <summary>
    /// Gets a list's items (movies and shows merged), each carrying the external ids MDBList records. Empty
    /// when no key is set or the list is empty.
    /// </summary>
    /// <param name="listId">The MDBList list id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list's items.</returns>
    public async Task<IReadOnlyList<MdbListItem>> GetListItemsAsync(int listId, CancellationToken cancellationToken)
    {
        var key = ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return Array.Empty<MdbListItem>();
        }

        var url = string.Create(CultureInfo.InvariantCulture, $"{BaseUrl}/lists/{listId}/items?apikey={key}");
        var response = await _api.GetJsonAsync<MdbListItemsResponse>(ServiceNames.MdbList, url, CachedApiClient.DefaultCacheDuration, _jsonOptions, null, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return Array.Empty<MdbListItem>();
        }

        var items = new List<MdbListItem>();
        AddItems(items, response.Movies, "movie");
        AddItems(items, response.Shows, "show");
        return items;
    }

    private static void AddItems(List<MdbListItem> into, IReadOnlyList<MdbListItem>? from, string mediaType)
    {
        if (from is null)
        {
            return;
        }

        foreach (var item in from)
        {
            if (string.IsNullOrEmpty(item.MediaType))
            {
                item.MediaType = mediaType;
            }

            into.Add(item);
        }
    }
}
