using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// Availability via TMDB's <c>watch/providers</c> endpoint. This data is JustWatch-sourced but
/// officially licensed through TMDB, making it the safe default. Region link only (no per-provider
/// deep-links).
/// </summary>
public sealed class TmdbAvailabilitySource : IAvailabilitySource
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TmdbAvailabilitySource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbAvailabilitySource"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public TmdbAvailabilitySource(IHttpClientFactory httpClientFactory, ILogger<TmdbAvailabilitySource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "TMDB";

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.IncludeAvailability;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailabilityOffer>> GetOffersAsync(AvailabilityQuery query, CancellationToken cancellationToken)
    {
        if (!query.ProviderIds.TryGetValue("Tmdb", out var tmdbStr)
            || !int.TryParse(tmdbStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
        {
            return Array.Empty<AvailabilityOffer>();
        }

        var path = query.TargetKind == BaseItemKind.Series ? "tv" : "movie";
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"https://api.themoviedb.org/3/{path}/{tmdbId}/watch/providers?api_key={TmdbClient.ResolveApiKey()}");

        TmdbWatchResponse? response;
        try
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var http = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!http.IsSuccessStatusCode)
            {
                _logger.LogWarning("TMDB watch/providers for {Id} returned {Status}", tmdbId, http.StatusCode);
                return Array.Empty<AvailabilityOffer>();
            }

            var stream = await http.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                response = await JsonSerializer.DeserializeAsync<TmdbWatchResponse>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TMDB watch/providers for {Id} failed", tmdbId);
            return Array.Empty<AvailabilityOffer>();
        }

        return TmdbWatchMapper.Map(response, query.Country);
    }
}
