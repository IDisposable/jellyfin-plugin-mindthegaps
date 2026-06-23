using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Acquisition;

/// <summary>
/// Hands a gap off to a configured acquisition stack: a movie to Radarr, a series (or a series' missing
/// episodes) to Sonarr, or any title to Jellyseerr/Overseerr as a request. Every send is opt-in (the
/// dashboard only shows a button for a configured target), keyed purely on ids the gap already carries (a
/// TMDB id for Radarr and Jellyseerr; the owning series' TheTVDB id, resolved from the library, for Sonarr),
/// and best-effort: an unreachable service or a rejected request is reported as a failed
/// <see cref="AcquisitionResult"/> rather than thrown, so one bad send never aborts a batch.
/// </summary>
public sealed class AcquisitionService
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<AcquisitionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcquisitionService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="libraryManager">The library manager, for resolving an owned series' TheTVDB id.</param>
    /// <param name="logger">The logger.</param>
    public AcquisitionService(IHttpClientFactory httpClientFactory, ILibraryManager libraryManager, ILogger<AcquisitionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether Radarr is fully configured (URL, key, quality profile, root folder).
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>True when a movie can be sent to Radarr.</returns>
    public static bool RadarrConfigured(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return !string.IsNullOrWhiteSpace(config.RadarrUrl)
            && !string.IsNullOrWhiteSpace(config.RadarrApiKey)
            && config.RadarrQualityProfileId > 0
            && !string.IsNullOrWhiteSpace(config.RadarrRootFolderPath);
    }

    /// <summary>
    /// Gets a value indicating whether Sonarr is fully configured (URL, key, quality profile, root folder).
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>True when a series can be sent to Sonarr.</returns>
    public static bool SonarrConfigured(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return !string.IsNullOrWhiteSpace(config.SonarrUrl)
            && !string.IsNullOrWhiteSpace(config.SonarrApiKey)
            && config.SonarrQualityProfileId > 0
            && !string.IsNullOrWhiteSpace(config.SonarrRootFolderPath);
    }

    /// <summary>
    /// Gets a value indicating whether Jellyseerr/Overseerr is configured (URL and key).
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>True when a title can be requested in Jellyseerr.</returns>
    public static bool SeerrConfigured(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return !string.IsNullOrWhiteSpace(config.SeerrUrl) && !string.IsNullOrWhiteSpace(config.SeerrApiKey);
    }

    /// <summary>
    /// Sends a gap to the matching arr: a movie gap to Radarr (by TMDB id), a series or episode gap to Sonarr
    /// (by the owning series' TheTVDB id, which adds the series so Sonarr grabs its missing episodes).
    /// </summary>
    /// <param name="gap">The gap to send.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<AcquisitionResult> SendToArrAsync(GapItem gap, PluginConfiguration config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gap);
        ArgumentNullException.ThrowIfNull(config);

        if (gap.TargetKind == BaseItemKind.Movie)
        {
            if (!RadarrConfigured(config))
            {
                return AcquisitionResult.Fail("Radarr is not configured.");
            }

            var tmdbId = ResolveTmdbId(gap);
            if (tmdbId is null)
            {
                return AcquisitionResult.Fail("This movie has no TMDB id to send to Radarr.");
            }

            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["title"] = gap.Name,
                ["tmdbId"] = tmdbId.Value,
                ["qualityProfileId"] = config.RadarrQualityProfileId,
                ["rootFolderPath"] = config.RadarrRootFolderPath,
                ["monitored"] = true,
                ["addOptions"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["searchForMovie"] = true }
            };
            return await PostAsync(config.RadarrUrl, "/api/v3/movie", config.RadarrApiKey, payload, "Radarr", "Sent to Radarr.", cancellationToken).ConfigureAwait(false);
        }

        if (!SonarrConfigured(config))
        {
            return AcquisitionResult.Fail("Sonarr is not configured.");
        }

        var tvdbId = ResolveSeriesTvdbId(gap);
        if (tvdbId is null)
        {
            return AcquisitionResult.Fail("This series has no TheTVDB id, which Sonarr needs.");
        }

        var monitor = string.IsNullOrWhiteSpace(config.SonarrMonitor) ? "all" : config.SonarrMonitor;
        var seriesPayload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["title"] = gap.SourceItemName ?? gap.Name,
            ["tvdbId"] = tvdbId.Value,
            ["qualityProfileId"] = config.SonarrQualityProfileId,
            ["rootFolderPath"] = config.SonarrRootFolderPath,
            ["monitored"] = true,
            ["addOptions"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["monitor"] = monitor,
                ["searchForMissingEpisodes"] = true
            }
        };
        return await PostAsync(config.SonarrUrl, "/api/v3/series", config.SonarrApiKey, seriesPayload, "Sonarr", "Sent to Sonarr.", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests a gap in Jellyseerr/Overseerr by TMDB id (a movie request for a movie gap, a series request
    /// for anything else). An episode gap requests its owning series.
    /// </summary>
    /// <param name="gap">The gap to request.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<AcquisitionResult> SendToSeerrAsync(GapItem gap, PluginConfiguration config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gap);
        ArgumentNullException.ThrowIfNull(config);

        if (!SeerrConfigured(config))
        {
            return AcquisitionResult.Fail("Jellyseerr is not configured.");
        }

        var tmdbId = ResolveTmdbId(gap);
        if (tmdbId is null)
        {
            return AcquisitionResult.Fail("This title has no TMDB id to request.");
        }

        var mediaType = gap.TargetKind == BaseItemKind.Movie ? "movie" : "tv";
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["mediaType"] = mediaType,
            ["mediaId"] = tmdbId.Value
        };
        return await PostAsync(config.SeerrUrl, "/api/v1/request", config.SeerrApiKey, payload, "Jellyseerr", "Requested in Jellyseerr.", cancellationToken).ConfigureAwait(false);
    }

    // The movie/series TMDB id: a movie gap carries it in ProviderIds; an episode/series gap carries the
    // owning series' id in WatchTmdbId (the same id the availability lookup uses).
    private static int? ResolveTmdbId(GapItem gap)
        => ParseId(GetProviderId(gap, "Tmdb")) ?? ParseId(gap.WatchTmdbId);

    private static int? ParseId(string? raw)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

    private static string? GetProviderId(GapItem gap, string key)
    {
        foreach (var pair in gap.ProviderIds)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private async Task<AcquisitionResult> PostAsync(string baseUrl, string path, string apiKey, IReadOnlyDictionary<string, object?> payload, string service, string successMessage, CancellationToken cancellationToken)
    {
        var url = baseUrl.TrimEnd('/') + path;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            return AcquisitionResult.Fail(string.Create(CultureInfo.InvariantCulture, $"{service} URL is not a valid http(s) address."));
        }

        try
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return AcquisitionResult.Ok(successMessage);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("{Service} returned {Status} for {Path}", service, (int)response.StatusCode, path);

            // A 4xx is common and expected (already requested, already owned, no matching item); report the
            // service's own message so the user sees why.
            var summary = AcquisitionResult.Summarize(body);
            return AcquisitionResult.Fail(string.IsNullOrEmpty(summary)
                ? string.Create(CultureInfo.InvariantCulture, $"{service} returned {(int)response.StatusCode}.")
                : string.Create(CultureInfo.InvariantCulture, $"{service} returned {(int)response.StatusCode}. {summary}"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Service} send failed", service);
            return AcquisitionResult.Fail(string.Create(CultureInfo.InvariantCulture, $"Could not reach {service}. Check the URL and that it is running."));
        }
    }

    // Sonarr is keyed on the series' TheTVDB id. A series-content gap stores the owned series' guid in
    // SourceItemId, so resolve the live library item and read its TheTVDB id; fall back to a Tvdb id on the
    // gap itself (a whole-series gap from a cross-check).
    private int? ResolveSeriesTvdbId(GapItem gap)
    {
        if (Guid.TryParse(gap.SourceItemId, out var seriesId) && seriesId != Guid.Empty)
        {
            var series = _libraryManager.GetItemById(seriesId);
            if (series is not null
                && series.TryGetProviderId(MetadataProvider.Tvdb, out var tvdb)
                && ParseId(tvdb) is int fromLibrary)
            {
                return fromLibrary;
            }
        }

        return ParseId(GetProviderId(gap, "Tvdb"));
    }
}
