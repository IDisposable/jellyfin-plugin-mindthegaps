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
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Acquisition;

/// <summary>
/// Hands a gap off to a configured acquisition stack: a movie to Radarr, a series (or a series' missing
/// episodes) to Sonarr, or any title to Jellyseerr/Overseerr as a request. Every send is opt-in (the
/// dashboard only shows a button for a configured target), keyed purely on ids the gap already carries (a
/// TMDB id for Radarr and Jellyseerr; a series' TheTVDB id for Sonarr, resolved from the owning library series
/// for an episode gap or from TMDB's external ids for a whole-series gap),
/// and best-effort: an unreachable service or a rejected request is reported as a failed
/// <see cref="AcquisitionResult"/> rather than thrown, so one bad send never aborts a batch.
/// </summary>
public sealed class AcquisitionService
{
    private const string PresenceCacheKey = "mtg-arr-presence";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan _presenceTtl = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly TmdbClient _tmdb;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AcquisitionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcquisitionService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="libraryManager">The library manager, for resolving an owned series' TheTVDB id.</param>
    /// <param name="tmdb">The TMDB client, for resolving an unowned series' TheTVDB id from its TMDB id.</param>
    /// <param name="cache">The memory cache, for the arrs' libraries.</param>
    /// <param name="logger">The logger.</param>
    public AcquisitionService(IHttpClientFactory httpClientFactory, ILibraryManager libraryManager, TmdbClient tmdb, IMemoryCache cache, ILogger<AcquisitionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _libraryManager = libraryManager;
        _tmdb = tmdb;
        _cache = cache;
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
    /// <param name="qualityProfileId">A quality profile to use instead of the configured default, when the
    /// caller offered a choice (the person page); <see langword="null"/> or non-positive keeps the default.</param>
    /// <returns>The outcome.</returns>
    public async Task<AcquisitionResult> SendToArrAsync(GapItem gap, PluginConfiguration config, CancellationToken cancellationToken, int? qualityProfileId = null)
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
                _logger.LogWarning("Radarr send skipped: '{Name}' has no TMDB id", gap.Name);
                return AcquisitionResult.Fail("This movie has no TMDB id to send to Radarr.");
            }

            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["title"] = gap.Name,
                ["tmdbId"] = tmdbId.Value,
                ["qualityProfileId"] = ChooseProfile(qualityProfileId, config.RadarrQualityProfileId),
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

        var seriesTitle = SeriesTitle(gap);
        var tvdbId = await ResolveSeriesTvdbIdAsync(gap, cancellationToken).ConfigureAwait(false);
        if (tvdbId is null)
        {
            _logger.LogWarning("Sonarr send skipped: '{Series}' has no TheTVDB id", seriesTitle);
            return AcquisitionResult.Fail("This series has no TheTVDB id, which Sonarr needs.");
        }

        var monitor = string.IsNullOrWhiteSpace(config.SonarrMonitor) ? "all" : config.SonarrMonitor;
        var seriesPayload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["title"] = seriesTitle,
            ["tvdbId"] = tvdbId.Value,
            ["qualityProfileId"] = ChooseProfile(qualityProfileId, config.SonarrQualityProfileId),
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
    /// Lists the quality profiles each configured arr offers, so a caller can let the user choose one for a
    /// send. A target that is not configured, or does not answer, contributes an empty list rather than an
    /// error: the choice is a convenience over the configured default, not a requirement.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The profiles per target and the configured defaults.</returns>
    public async Task<AcquisitionProfiles> GetQualityProfilesAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        var result = new AcquisitionProfiles { RadarrDefault = config.RadarrQualityProfileId, SonarrDefault = config.SonarrQualityProfileId };
        if (RadarrConfigured(config))
        {
            result.Radarr = await GetProfilesAsync(config.RadarrUrl, config.RadarrApiKey, "Radarr", cancellationToken).ConfigureAwait(false);
        }

        if (SonarrConfigured(config))
        {
            result.Sonarr = await GetProfilesAsync(config.SonarrUrl, config.SonarrApiKey, "Sonarr", cancellationToken).ConfigureAwait(false);
        }

        return result;
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
            _logger.LogWarning("Jellyseerr request skipped: '{Name}' has no TMDB id", gap.Name);
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

    /// <summary>
    /// The series title Sonarr is given. A whole-series gap (a filmography, recommendation, or favorites
    /// entry) is the series itself, so its own name; an episode or season gap belongs to the owned series
    /// named in its source, so that name.
    /// </summary>
    /// <param name="gap">The gap.</param>
    /// <returns>The title.</returns>
    public static string SeriesTitle(GapItem gap)
    {
        ArgumentNullException.ThrowIfNull(gap);
        return gap.TargetKind == BaseItemKind.Series ? gap.Name : gap.SourceItemName ?? gap.Name;
    }

    // The movie/series TMDB id: a movie gap carries it in ProviderIds; an episode/series gap carries the
    // owning series' id in WatchTmdbId (the same id the availability lookup uses).
    private static int? ResolveTmdbId(GapItem gap)
        => ParseId(GetProviderId(gap, ProviderIds.Tmdb)) ?? ParseId(gap.WatchTmdbId);

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

    /// <summary>
    /// What each configured arr already holds, keyed by TMDB id, so a surface can show "in Radarr" instead of
    /// offering a send that would be rejected. One read of each arr's library, cached briefly; an arr that
    /// is not configured or does not answer contributes nothing.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The presence, by TMDB id, for movies and for series.</returns>
    public async Task<ArrPresence> GetPresenceAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        var cached = await _cache.GetOrCreateAsync(PresenceCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _presenceTtl;
            var presence = new ArrPresence();
            if (RadarrConfigured(config))
            {
                presence.Movies = await ReadPresenceAsync(config.RadarrUrl, config.RadarrApiKey, "/api/v3/movie", "Radarr", cancellationToken).ConfigureAwait(false);
            }

            if (SonarrConfigured(config))
            {
                presence.Series = await ReadPresenceAsync(config.SonarrUrl, config.SonarrApiKey, "/api/v3/series", "Sonarr", cancellationToken).ConfigureAwait(false);
            }

            return presence;
        }).ConfigureAwait(false);
        return cached ?? new ArrPresence();
    }

    /// <summary>
    /// Forgets the cached arr libraries, after a send has changed them.
    /// </summary>
    public void ForgetPresence() => _cache.Remove(PresenceCacheKey);

    /// <summary>
    /// Reads an arr's library list into presence by TMDB id: for each entry with a positive integer
    /// <c>tmdbId</c>, whether it has a file (<c>hasFile</c> for a movie, any <c>statistics.episodeFileCount</c>
    /// for a series) and whether it is <c>monitored</c>.
    /// </summary>
    /// <param name="root">The JSON array.</param>
    /// <returns>The presence by TMDB id.</returns>
    public static IReadOnlyDictionary<int, ArrItemState> ParsePresence(JsonElement root)
    {
        var result = new Dictionary<int, ArrItemState>();
        if (root.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("tmdbId", out var idProp)
                || idProp.ValueKind != JsonValueKind.Number
                || !idProp.TryGetInt32(out var id)
                || id <= 0)
            {
                continue;
            }

            var hasFile = element.TryGetProperty("hasFile", out var hf) && hf.ValueKind == JsonValueKind.True;
            if (!hasFile
                && element.TryGetProperty("statistics", out var stats)
                && stats.ValueKind == JsonValueKind.Object
                && stats.TryGetProperty("episodeFileCount", out var count)
                && count.ValueKind == JsonValueKind.Number
                && count.TryGetInt32(out var files))
            {
                hasFile = files > 0;
            }

            var monitored = element.TryGetProperty("monitored", out var mon) && mon.ValueKind == JsonValueKind.True;
            result[id] = new ArrItemState { HasFile = hasFile, Monitored = monitored };
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<int, ArrItemState>> ReadPresenceAsync(string baseUrl, string apiKey, string path, string service, CancellationToken cancellationToken)
    {
        var url = baseUrl.TrimEnd('/') + path;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new Dictionary<int, ArrItemState>();
        }

        var logUrl = LogSafe.Redact(uri.ToString());
        try
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Service}: {Status} from GET {Url}", service, (int)response.StatusCode, logUrl);
                return new Dictionary<int, ArrItemState>();
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ParsePresence(doc.RootElement);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Service}: could not read the library from GET {Url}", service, logUrl);
            return new Dictionary<int, ArrItemState>();
        }
    }

    private static int ChooseProfile(int? requested, int configured)
        => requested is > 0 ? requested.Value : configured;

    // Both arrs expose the same shape at the same path: [{ id, name, ... }].
    private async Task<IReadOnlyList<QualityProfileChoice>> GetProfilesAsync(string baseUrl, string apiKey, string service, CancellationToken cancellationToken)
    {
        var url = baseUrl.TrimEnd('/') + "/api/v3/qualityprofile";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return [];
        }

        var logUrl = LogSafe.Redact(uri.ToString());
        try
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Service}: {Status} from GET {Url}", service, (int)response.StatusCode, logUrl);
                return [];
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ParseProfiles(doc.RootElement);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Service}: could not list quality profiles from GET {Url}", service, logUrl);
            return [];
        }
    }

    /// <summary>
    /// Reads an arr's quality-profile list: every element with a positive integer <c>id</c> and a non-empty
    /// <c>name</c>, in the order given. Anything else is skipped.
    /// </summary>
    /// <param name="root">The JSON array.</param>
    /// <returns>The profiles.</returns>
    public static IReadOnlyList<QualityProfileChoice> ParseProfiles(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var profiles = new List<QualityProfileChoice>();
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("id", out var idProp)
                || idProp.ValueKind != JsonValueKind.Number
                || !idProp.TryGetInt32(out var id)
                || id <= 0
                || !element.TryGetProperty("name", out var nameProp)
                || nameProp.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = nameProp.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            profiles.Add(new QualityProfileChoice { Id = id, Name = name });
        }

        return profiles;
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

        // The base URL is the user's, so it can carry basic-auth credentials; only the redacted form is logged.
        var logUrl = LogSafe.Redact(uri.ToString());

        try
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            if (Plugin.DetailedApiLogging)
            {
                _logger.LogDebug("{Service}: POST {Url} body {Body}", service, logUrl, json);
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                if (Plugin.DetailedApiLogging)
                {
                    _logger.LogDebug("{Service}: {Status} accepted POST {Url}", service, (int)response.StatusCode, logUrl);
                }

                return AcquisitionResult.Ok(successMessage);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("{Service}: {Status} from POST {Url} body {Body}", service, (int)response.StatusCode, logUrl, body);

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
            _logger.LogWarning(ex, "{Service} send failed for POST {Url}", service, logUrl);
            return AcquisitionResult.Fail(string.Create(CultureInfo.InvariantCulture, $"Could not reach {service}. Check the URL and that it is running."));
        }
    }

    // Sonarr is keyed on the series' TheTVDB id. An episode gap stores the owned series' guid in SourceItemId,
    // so resolve the live library item and read its TheTVDB id. A whole-series gap's source is not a series
    // (a person, a recommending title), so fall back to a Tvdb id already on the gap (merged in by the
    // availability pass), and past that ask TMDB for the series' external ids, which is the one call that
    // turns the TMDB id every series gap carries into the id Sonarr needs.
    private async Task<int?> ResolveSeriesTvdbIdAsync(GapItem gap, CancellationToken cancellationToken)
    {
        if (gap.TargetKind != BaseItemKind.Series
            && Guid.TryParse(gap.SourceItemId, out var seriesId)
            && seriesId != Guid.Empty)
        {
            var series = _libraryManager.GetItemById(seriesId);
            if (series is not null
                && series.TryGetProviderId(ProviderIds.Tvdb, out var tvdb)
                && ParseId(tvdb) is int fromLibrary)
            {
                return fromLibrary;
            }
        }

        if (ParseId(GetProviderId(gap, ProviderIds.Tvdb)) is int onGap)
        {
            return onGap;
        }

        if (gap.TargetKind == BaseItemKind.Series && ResolveTmdbId(gap) is int tmdbId)
        {
            var (_, tvdbFromTmdb) = await _tmdb.GetExternalIdsAsync(tmdbId, isSeries: true, cancellationToken).ConfigureAwait(false);
            return ParseId(tvdbFromTmdb);
        }

        return null;
    }
}
