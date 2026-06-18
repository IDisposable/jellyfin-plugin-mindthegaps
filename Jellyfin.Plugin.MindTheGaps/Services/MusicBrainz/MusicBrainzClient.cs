using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.MusicBrainz;

/// <summary>
/// A minimal client for the MusicBrainz public, key-free JSON web service (ws/2). MusicBrainz asks
/// callers to send a descriptive User-Agent and to keep below one request per second, so the caller
/// caps how many artists it scans per run. See https://musicbrainz.org/doc/MusicBrainz_API.
/// </summary>
public sealed class MusicBrainzClient
{
    private const string BaseUrl = "https://musicbrainz.org/ws/2";

    // MusicBrainz returns at most 100 entities per browse page; guard against runaway paging.
    private const int PageSize = 100;
    private const int MaxPages = 20;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MusicBrainzClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicBrainzClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public MusicBrainzClient(IHttpClientFactory httpClientFactory, ILogger<MusicBrainzClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Browses every album-typed release-group for an artist, paging through the result set.
    /// </summary>
    /// <param name="artistMbid">The artist MBID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The release-groups, or an empty list on failure.</returns>
    public async Task<IReadOnlyList<MusicBrainzReleaseGroup>> GetArtistAlbumsAsync(string artistMbid, CancellationToken cancellationToken)
    {
        var groups = new List<MusicBrainzReleaseGroup>();
        for (var page = 0; page < MaxPages; page++)
        {
            var offset = page * PageSize;
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"/release-group?artist={Uri.EscapeDataString(artistMbid)}&type=album&limit={PageSize}&offset={offset}&fmt=json");

            var response = await GetAsync<MusicBrainzReleaseGroupResponse>(path, cancellationToken).ConfigureAwait(false);
            var pageGroups = response?.ReleaseGroups;
            if (pageGroups is null || pageGroups.Count == 0)
            {
                break;
            }

            groups.AddRange(pageGroups);

            if (groups.Count >= response!.ReleaseGroupCount || groups.Count >= MaxPages * PageSize)
            {
                break;
            }
        }

        return groups;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);

            // MusicBrainz blocks requests without a meaningful User-Agent (see their API docs).
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Jellyfin.Plugin.MindTheGaps", "1.0"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/IDisposable/jellyfin-plugin-mindthegaps)"));

            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MusicBrainz GET {Path} returned {Status}", path, response.StatusCode);
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
            _logger.LogWarning(ex, "MusicBrainz GET {Path} failed", path);
            return default;
        }
    }
}
