using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// A minimal client for the Discogs HTTP API. Discogs requires authentication (a personal access token)
/// to browse the catalog and a descriptive User-Agent (which HttpRetry adds). The token comes from the
/// plugin configuration. See https://www.discogs.com/developers.
/// </summary>
internal sealed class DiscogsClient
{
    private const string BaseUrl = "https://api.discogs.com";

    // Discogs returns up to 100 items per page; cap paging so a large label does not run away.
    private const int PageSize = 100;
    private const int MaxPages = 20;

    // The artist releases endpoint mixes masters (release groups) with every individual pressing and guest
    // appearance, and has no server-side type filter, so we keep only the masters and cap the paging tighter
    // than the label browse. Discogs is paced at roughly one request a second, so an artist with thousands of
    // pressings would otherwise cost many seconds of paging for rows we only discard.
    private const int ArtistReleasesMaxPages = 5;

    // The settings type-ahead shows a short list, so a partial query does not flood the dropdown.
    private const int MaxSuggestions = 10;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CachedApiClient _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscogsClient"/> class.
    /// </summary>
    /// <param name="api">The cached API client.</param>
    public DiscogsClient(CachedApiClient api)
    {
        _api = api;
    }

    /// <summary>
    /// Searches Discogs labels by name for the settings type-ahead, returning the top matches as id and name
    /// pairs (the empty result included).
    /// </summary>
    /// <param name="query">The partial label name typed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The top matches.</returns>
    public async Task<IReadOnlyList<CuratedSetRef>> SearchLabelsAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var path = string.Create(CultureInfo.InvariantCulture, $"/database/search?type=label&q={Uri.EscapeDataString(query)}&per_page={MaxSuggestions}");
        var response = await GetAsync<DiscogsSearchResponse>(path, CachedApiClient.DefaultCacheDuration, cancellationToken).ConfigureAwait(false);
        var refs = new List<CuratedSetRef>();
        foreach (var result in response?.Results ?? new List<DiscogsSearchResult>())
        {
            if (result.Id > 0 && !string.IsNullOrEmpty(result.Title))
            {
                refs.Add(new CuratedSetRef { Id = (int)result.Id, Name = result.Title });
            }

            if (refs.Count >= MaxSuggestions)
            {
                break;
            }
        }

        return refs;
    }

    /// <summary>
    /// Gets a label's display name by its Discogs id, so a stored label id renders as a named chip.
    /// </summary>
    /// <param name="labelId">The Discogs label id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The label name, or null.</returns>
    public async Task<string?> GetLabelNameAsync(long labelId, CancellationToken cancellationToken)
    {
        // A label name is stable, so cache it for far longer than a scan; this also backs the chip picker's
        // CuratedResolve, which would otherwise re-fetch every name each time the settings page loads.
        var label = await GetAsync<DiscogsLabel>(
            string.Create(CultureInfo.InvariantCulture, $"/labels/{labelId}"),
            CachedApiClient.StableCacheDuration,
            cancellationToken).ConfigureAwait(false);
        return label?.Name;
    }

    /// <summary>
    /// Browses every release on a Discogs label, paging through the result set up to the page cap.
    /// </summary>
    /// <param name="labelId">The Discogs label id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The label's releases, or an empty list on failure.</returns>
    public async Task<IReadOnlyList<DiscogsRelease>> GetLabelReleasesAsync(long labelId, CancellationToken cancellationToken)
    {
        var releases = new List<DiscogsRelease>();
        for (var page = 1; page <= MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = string.Create(CultureInfo.InvariantCulture, $"/labels/{labelId}/releases?per_page={PageSize}&page={page}");
            var response = await GetAsync<DiscogsLabelReleasesResponse>(path, CachedApiClient.DefaultCacheDuration, cancellationToken).ConfigureAwait(false);
            var pageReleases = response?.Releases;
            if (pageReleases is null || pageReleases.Count == 0)
            {
                break;
            }

            releases.AddRange(pageReleases);

            var totalPages = response!.Pagination?.Pages ?? page;
            if (page >= totalPages)
            {
                break;
            }
        }

        return releases;
    }

    /// <summary>
    /// Browses a Discogs artist's discography and returns only the artist's own master releases (one entry per
    /// album, the release-group equivalent), not the individual pressings or guest appearances. Discogs has no
    /// server-side type filter, so the masters are sieved out of each page as it is read and the paging is
    /// capped tighter than the label browse.
    /// </summary>
    /// <param name="artistId">The Discogs artist id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The artist's master releases (release groups), or an empty list on failure.</returns>
    public async Task<IReadOnlyList<DiscogsRelease>> GetArtistReleasesAsync(long artistId, CancellationToken cancellationToken)
    {
        var masters = new List<DiscogsRelease>();
        for (var page = 1; page <= ArtistReleasesMaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = string.Create(CultureInfo.InvariantCulture, $"/artists/{artistId}/releases?sort=year&per_page={PageSize}&page={page}");
            var response = await GetAsync<DiscogsLabelReleasesResponse>(path, CachedApiClient.DefaultCacheDuration, cancellationToken).ConfigureAwait(false);
            var pageReleases = response?.Releases;
            if (pageReleases is null || pageReleases.Count == 0)
            {
                break;
            }

            // Keep one entry per album (a master) credited to the artist as a main work, dropping every
            // individual pressing and guest appearance before they are carried any further.
            foreach (var release in pageReleases)
            {
                if (string.Equals(release.Type, "master", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(release.Role, "Main", StringComparison.OrdinalIgnoreCase))
                {
                    masters.Add(release);
                }
            }

            var totalPages = response!.Pagination?.Pages ?? page;
            if (page >= totalPages)
            {
                break;
            }
        }

        return masters;
    }

    private Task<T?> GetAsync<T>(string path, TimeSpan cacheDuration, CancellationToken cancellationToken)
        where T : class
    {
        // Discogs requires a personal access token to browse; without one configured, nothing to fetch.
        var token = Plugin.Instance?.Configuration.DiscogsToken;
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult<T?>(null);
        }

        return _api.GetJsonAsync<T>(
            ServiceNames.Discogs,
            BaseUrl + path,
            cacheDuration,
            _jsonOptions,
            // "Discogs" here is the HTTP auth scheme Discogs requires ("Authorization: Discogs token=..."),
            // not the service name.
            request => request.Headers.Authorization = new AuthenticationHeaderValue(
                "Discogs",
                string.Create(CultureInfo.InvariantCulture, $"token={token}")),
            cancellationToken);
    }
}
