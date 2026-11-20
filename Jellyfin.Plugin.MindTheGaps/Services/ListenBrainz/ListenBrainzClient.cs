using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;

namespace Jellyfin.Plugin.MindTheGaps.Services.ListenBrainz;

/// <summary>
/// A minimal client for ListenBrainz's keyless similar-artists endpoint, which answers with each similar
/// artist's MusicBrainz id and name in one call.
/// </summary>
internal sealed class ListenBrainzClient
{
    private const string BaseUrl = "https://labs.api.listenbrainz.org/similar-artists/json";

    private const string Algorithm = "session_based_days_7500_session_300_contribution_5_threshold_10_limit_100_filter_True_skip_30";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CachedApiClient _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListenBrainzClient"/> class.
    /// </summary>
    /// <param name="api">The cached API client.</param>
    public ListenBrainzClient(CachedApiClient api)
    {
        _api = api;
    }

    /// <summary>
    /// Gets the artists similar to one artist, closest first.
    /// </summary>
    /// <param name="artistMbid">The artist's MusicBrainz id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The similar artists, or null when the service could not answer.</returns>
    public Task<List<ListenBrainzSimilarArtist>?> GetSimilarArtistsAsync(string artistMbid, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artistMbid);
        var url = string.Create(CultureInfo.InvariantCulture, $"{BaseUrl}?artist_mbids={Uri.EscapeDataString(artistMbid.Trim())}&algorithm={Algorithm}");
        return _api.GetJsonAsync<List<ListenBrainzSimilarArtist>>(
            ServiceNames.ListenBrainz,
            url,
            CachedApiClient.DefaultCacheDuration,
            _jsonOptions,
            null,
            cancellationToken);
    }
}
