using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;

namespace Jellyfin.Plugin.MindTheGaps.Services.MusicBrainz;

/// <summary>
/// A minimal client for the MusicBrainz public, key-free JSON web service (ws/2). MusicBrainz asks
/// callers to send a descriptive User-Agent and to keep below one request per second, so the caller
/// caps how many artists it scans per run. See https://musicbrainz.org/doc/MusicBrainz_API.
/// </summary>
internal sealed class MusicBrainzClient
{
    private const string BaseUrl = "https://musicbrainz.org/ws/2";

    // MusicBrainz returns at most 100 entities per browse page; guard against runaway paging.
    private const int PageSize = 100;
    private const int MaxPages = 20;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CachedApiClient _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicBrainzClient"/> class.
    /// </summary>
    /// <param name="api">The cached API client.</param>
    public MusicBrainzClient(CachedApiClient api)
    {
        _api = api;
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

    // CachedApiClient caches the result and adds the descriptive User-Agent (with the plugin version)
    // MusicBrainz requires, via the shared HttpRetry path.
    private Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
        => _api.GetJsonAsync<T>(ServiceNames.MusicBrainz, BaseUrl + path, CachedApiClient.DefaultCacheDuration, _jsonOptions, null, cancellationToken);
}
