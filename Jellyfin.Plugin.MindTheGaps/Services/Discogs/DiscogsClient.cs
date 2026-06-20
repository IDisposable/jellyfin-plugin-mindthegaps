using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Discogs;

/// <summary>
/// A minimal client for the Discogs HTTP API. Discogs requires authentication (a personal access token)
/// to browse the catalogue and a descriptive User-Agent (which HttpRetry adds). The token comes from the
/// plugin configuration. See https://www.discogs.com/developers.
/// </summary>
public sealed class DiscogsClient
{
    private const string BaseUrl = "https://api.discogs.com";

    // Discogs returns up to 100 items per page; cap paging so a large label does not run away.
    private const int PageSize = 100;
    private const int MaxPages = 20;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DiscogsClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscogsClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public DiscogsClient(IHttpClientFactory httpClientFactory, ILogger<DiscogsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a label name to its Discogs id (the first label result), or null when nothing matches.
    /// </summary>
    /// <param name="name">The label name to search for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The label id, or null.</returns>
    public async Task<long?> SearchLabelAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var path = string.Create(CultureInfo.InvariantCulture, $"/database/search?type=label&q={Uri.EscapeDataString(name)}&per_page=5");
        var response = await GetAsync<DiscogsSearchResponse>(path, cancellationToken).ConfigureAwait(false);
        if (response?.Results is null)
        {
            return null;
        }

        foreach (var result in response.Results)
        {
            if (result.Id > 0)
            {
                return result.Id;
            }
        }

        return null;
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
            var response = await GetAsync<DiscogsLabelReleasesResponse>(path, cancellationToken).ConfigureAwait(false);
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

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var token = Plugin.Instance?.Configuration.DiscogsToken;
        if (string.IsNullOrEmpty(token))
        {
            return default;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var response = await HttpRetry.SendAsync(
                client,
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        "Discogs",
                        string.Create(CultureInfo.InvariantCulture, $"token={token}"));
                    return request;
                },
                _logger,
                "Discogs",
                path,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Discogs GET {Path} returned {Status}", path, response.StatusCode);
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
            _logger.LogWarning(ex, "Discogs GET {Path} failed", path);
            return default;
        }
    }
}
