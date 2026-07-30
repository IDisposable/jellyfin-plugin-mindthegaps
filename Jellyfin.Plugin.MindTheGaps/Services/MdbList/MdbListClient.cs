using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
internal sealed class MdbListClient
{
    private const string BaseUrl = "https://api.mdblist.com";
    private const int MaxSuggestions = 10;

    // MDBList's own default page size. Asking for it explicitly keeps the offset walk honest.
    private const int PageSize = 1000;

    // Bounds the walk. Well above any list the discovery source will emit from, so it is a runaway guard
    // rather than a cap the user meets.
    private const int MaxItems = 20000;

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
        var sanitized = SanitizeQuery(query);
        if (sanitized.Length == 0 || string.IsNullOrWhiteSpace(key))
        {
            return [];
        }

        var url = string.Create(CultureInfo.InvariantCulture, $"{BaseUrl}/lists/search?query={Uri.EscapeDataString(sanitized)}&apikey={key}");
        var lists = await _api.GetJsonAsync<List<MdbListListDto>>(ServiceNames.MdbList, url, CachedApiClient.DefaultCacheDuration, _jsonOptions, null, cancellationToken).ConfigureAwait(false);
        if (lists is null)
        {
            return [];
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
    /// Sanitizes a search-as-you-type query for MDBList's list search, which rejects certain punctuation
    /// (a stray semicolon, for instance, makes the whole request a BadRequest). Keeps letters, digits, and
    /// spaces and drops everything else (semicolons, control characters, other punctuation), then trims and
    /// collapses runs of whitespace to a single space. An all-punctuation query sanitizes to an empty string,
    /// which the caller treats as no query.
    /// </summary>
    /// <param name="query">The raw query typed.</param>
    /// <returns>The sanitized query, or an empty string when nothing usable remains.</returns>
    public static string SanitizeQuery(string? query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(query.Length);
        var pendingSpace = false;
        foreach (var ch in query)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
            }

            // Everything else (semicolons, control characters, other punctuation) is dropped.
        }

        return builder.ToString();
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
            return [];
        }

        return await PageAsync(
            offset => string.Create(CultureInfo.InvariantCulture, $"{BaseUrl}/lists/{listId}/items?apikey={key}&offset={offset}&limit={PageSize}"),
            CachedApiClient.DefaultCacheDuration,
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>
    /// Reads the account's own watchlist, which MDBList serves in the same envelope as a list's items.
    /// Returns null when there is no key or the call failed, so a caller can tell an empty watchlist from an
    /// unreachable one.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The watchlist items, or null.</returns>
    public async Task<IReadOnlyList<MdbListItem>?> GetWatchlistAsync(CancellationToken cancellationToken)
    {
        var key = ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        // A watchlist is edited between scans, so it is not held for the half-day a community list is.
        return await PageAsync(
            offset => string.Create(CultureInfo.InvariantCulture, $"{BaseUrl}/watchlist/items?apikey={key}&offset={offset}&limit={PageSize}"),
            TimeSpan.FromMinutes(10),
            cancellationToken).ConfigureAwait(false);
    }

    // Walks the offset pages until MDBList says there are no more. Its default page is large (1000), so most
    // lists are one call, but the response carries has_more and ignoring it silently truncated a longer one.
    // Returns null when the first page could not be read, so a caller can tell empty from unreachable.
    private async Task<IReadOnlyList<MdbListItem>?> PageAsync(
        Func<int, string> url,
        TimeSpan cacheDuration,
        CancellationToken cancellationToken)
    {
        var items = new List<MdbListItem>();

        for (var offset = 0; items.Count < MaxItems; offset += PageSize)
        {
            var response = await _api.GetJsonAsync<MdbListItemsResponse>(
                ServiceNames.MdbList,
                url(offset),
                cacheDuration,
                _jsonOptions,
                null,
                cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                return items.Count > 0 ? items : null;
            }

            AddItems(items, response.Movies, "movie");
            AddItems(items, response.Shows, "show");

            if (response.Pagination?.HasMore != true)
            {
                break;
            }
        }

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
