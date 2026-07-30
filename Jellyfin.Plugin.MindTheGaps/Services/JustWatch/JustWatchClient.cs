using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;

namespace Jellyfin.Plugin.MindTheGaps.Services.JustWatch;

/// <summary>
/// A minimal client for the GraphQL API justwatch.com itself reads from (https://apis.justwatch.com/graphql).
/// JustWatch's published Content Partner API covers streaming availability only, not accounts, so a personal
/// watchlist is reachable solely through this endpoint, and only with the account's own bearer token: the
/// query is rejected outright without one. The token is user-supplied (copied out of a signed-in browser
/// session) and treated as a secret.
/// </summary>
internal sealed class JustWatchClient
{
    // How many entries to ask for per page.
    private const int PageSize = 100;

    private const string Endpoint = "https://apis.justwatch.com/graphql";

    // The site's own origin. JustWatch rejects the query without it.
    private const string Origin = "https://www.justwatch.com";

    private const string ListQuery =
        "query MtgTitleList($country: Country!, $language: Language!, $listType: TitleListTypeV2!, $first: Int!, $after: String) " +
        "{ titleListV2(country: $country, titleListType: $listType, first: $first, after: $after) " +
        "{ totalCount pageInfo { hasNextPage endCursor } edges { node { __typename ... on MovieOrShow " +
        "{ id objectType content(country: $country, language: $language) " +
        "{ title originalReleaseYear fullPath posterUrl externalIds { imdbId tmdbId } } } } } } }";

    // A watchlist is edited between scans, so it is cached only long enough to de-duplicate the calls within
    // one scan and a quick re-scan.
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CachedApiClient _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="JustWatchClient"/> class.
    /// </summary>
    /// <param name="api">The cached API client.</param>
    public JustWatchClient(CachedApiClient api)
    {
        _api = api;
    }

    private static string? Token => Plugin.Instance?.Configuration.JustWatchToken;

    /// <summary>
    /// Reads every entry of one of the account's lists, following the cursor until the list ends or
    /// <paramref name="maxItems"/> is reached. Returns null when there is no token or the call failed, so a
    /// caller can tell "the list is empty" from "could not read it".
    /// </summary>
    /// <param name="listType">The list to read, a value from <see cref="JustWatchListType.All"/>.</param>
    /// <param name="country">The country code the entries are resolved for.</param>
    /// <param name="language">The language the titles are returned in.</param>
    /// <param name="maxItems">The most entries to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The titles on the list, or null.</returns>
    public async Task<IReadOnlyList<JustWatchTitle>?> GetListAsync(
        string listType,
        string country,
        string language,
        int maxItems,
        CancellationToken cancellationToken)
    {
        var token = Token;
        if (string.IsNullOrWhiteSpace(token) || !JustWatchListType.IsKnown(listType))
        {
            return null;
        }

        var titles = new List<JustWatchTitle>();
        string? cursor = null;

        while (titles.Count < maxItems)
        {
            var page = await FetchPageAsync(
                listType,
                country,
                language,
                Math.Min(PageSize, maxItems - titles.Count),
                cursor,
                token,
                cancellationToken).ConfigureAwait(false);
            if (page is null)
            {
                return titles.Count > 0 ? titles : null;
            }

            foreach (var edge in page.Edges ?? [])
            {
                if (edge.Node is { } title)
                {
                    titles.Add(title);
                }
            }

            cursor = page.PageInfo?.EndCursor;
            if (page.PageInfo?.HasNextPage != true || string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        return titles;
    }

    /// <summary>
    /// Expands a poster path into an image URL. JustWatch returns a template rather than a URL, with a
    /// {profile} size and a {format} extension to fill in.
    /// </summary>
    /// <param name="posterPath">The poster path from the API, or null.</param>
    /// <returns>The image URL, or null.</returns>
    public static string? PosterUrl(string? posterPath)
    {
        if (string.IsNullOrWhiteSpace(posterPath))
        {
            return null;
        }

        var path = posterPath
            .Replace("{profile}", "s332", StringComparison.Ordinal)
            .Replace("{format}", "jpg", StringComparison.Ordinal);
        return string.Create(CultureInfo.InvariantCulture, $"https://images.justwatch.com{path}");
    }

    /// <summary>
    /// Builds the justwatch.com address of a title from the path the API returns.
    /// </summary>
    /// <param name="fullPath">The title's path, or null.</param>
    /// <returns>The title URL, or null.</returns>
    public static string? TitleUrl(string? fullPath)
        => string.IsNullOrWhiteSpace(fullPath)
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{Origin}{fullPath}");

    private async Task<JustWatchTitleList?> FetchPageAsync(
        string listType,
        string country,
        string language,
        int first,
        string? after,
        string token,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            query = ListQuery,
            variables = new
            {
                country,
                language,
                listType,
                first,
                after
            }
        });

        var response = await _api.PostJsonAsync<JustWatchGraphResponse>(
            ServiceNames.JustWatch,
            Endpoint,
            body,
            _cacheDuration,
            _jsonOptions,
            request =>
            {
                request.Headers.Add("Authorization", string.Concat("Bearer ", token));
                request.Headers.Add("Origin", Origin);
            },
            cancellationToken).ConfigureAwait(false);

        return response?.Data?.TitleListV2;
    }
}
