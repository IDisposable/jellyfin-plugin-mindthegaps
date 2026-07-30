using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;

namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// A minimal client for the GraphQL API imdb.com itself reads from (https://api.graphql.imdb.com/). It needs
/// no key, only the client-name header the endpoint requires, and it serves any list or watchlist its owner
/// has made public. IMDb allows limited non-commercial use of this data; a private list stays private (the
/// API answers "permission denied"), so the plugin can only read what the account has chosen to publish.
/// </summary>
internal sealed class ImdbClient
{
    // How many entries to ask for per page. IMDb accepts a large page, so a typical watchlist is one call.
    private const int PageSize = 250;

    private const string Endpoint = "https://api.graphql.imdb.com/";

    // The one required header. Any value is accepted; the endpoint 403s without it.
    private const string ClientNameHeader = "x-imdb-client-name";
    private const string ClientName = "mind-the-gaps";

    // A list item is a union. Both fragments are spread, so one query serves the titles source and the people
    // source and, with the shared cache, one fetch feeds both when both are enabled.
    private const string ItemsFragment =
        "items(first: $first, after: $after) { total pageInfo { hasNextPage endCursor } " +
        "edges { node { item { __typename ... on Title { id titleText { text } releaseYear { year } " +
        "titleType { id canHaveEpisodes } primaryImage { url } } " +
        "... on Name { id nameText { text } primaryImage { url } } } } } }";

    private const string ListQuery =
        "query MtgList($id: ID!, $first: Int!, $after: ID) { list(id: $id) { id name { originalText } listType { id } " +
        ItemsFragment + " } }";

    private const string WatchlistQuery =
        "query MtgWatchlist($id: ID!, $first: Int!, $after: ID) { predefinedList(classType: WATCH_LIST, userId: $id) " +
        "{ id name { originalText } listType { id } " + ItemsFragment + " } }";

    // A personal list is edited between scans, so it is cached only long enough to de-duplicate the calls
    // within one scan and a quick re-scan, not for the half-day a catalog lookup is held.
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CachedApiClient _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImdbClient"/> class.
    /// </summary>
    /// <param name="api">The cached API client.</param>
    public ImdbClient(CachedApiClient api)
    {
        _api = api;
    }

    /// <summary>
    /// Reads every entry of a list ("ls...") or of a user's watchlist ("ur..."), following the cursor until
    /// the list ends or <paramref name="maxItems"/> is reached. Returns null when the list is missing,
    /// private, or the service is unreachable, which is what lets a caller tell "nothing there" from "could
    /// not read it".
    /// </summary>
    /// <param name="id">A list id ("ls...") or a user id ("ur...", for that user's watchlist).</param>
    /// <param name="maxItems">The most entries to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list with its entries, or null.</returns>
    public async Task<ImdbListContents?> GetListAsync(string id, int maxItems, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var isUser = id.StartsWith("ur", StringComparison.OrdinalIgnoreCase);
        var query = isUser ? WatchlistQuery : ListQuery;
        var entries = new List<ImdbListEntry>();
        string? name = null;
        string? listId = null;
        string? listType = null;
        string? cursor = null;

        while (entries.Count < maxItems)
        {
            var page = await FetchPageAsync(query, id, Math.Min(PageSize, maxItems - entries.Count), cursor, isUser, cancellationToken)
                .ConfigureAwait(false);
            if (page is null)
            {
                // A failed first page is "could not read"; a failed later page still returns what was read.
                return entries.Count > 0 ? new ImdbListContents(listId ?? id, name, listType, entries) : null;
            }

            name ??= page.Name?.Value;
            listId ??= page.Id;
            listType ??= page.ListType?.Id;
            foreach (var edge in page.Items?.Edges ?? [])
            {
                if (edge.Node?.Item is { Id.Length: > 0 } entry)
                {
                    entries.Add(entry);
                }
            }

            cursor = page.Items?.PageInfo?.EndCursor;
            if (page.Items?.PageInfo?.HasNextPage != true || string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        return new ImdbListContents(listId ?? id, name, listType, entries);
    }

    private async Task<ImdbList?> FetchPageAsync(
        string query,
        string id,
        int first,
        string? after,
        bool isUser,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            query,
            variables = new { id, first, after }
        });

        var response = await _api.PostJsonAsync<ImdbGraphResponse>(
            ServiceNames.Imdb,
            Endpoint,
            body,
            _cacheDuration,
            _jsonOptions,
            request => request.Headers.Add(ClientNameHeader, ClientName),
            cancellationToken).ConfigureAwait(false);

        return isUser ? response?.Data?.PredefinedList : response?.Data?.List;
    }
}
