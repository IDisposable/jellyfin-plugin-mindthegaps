using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Webhook;

/// <summary>
/// Posts a small JSON payload to a configured webhook URL when a scan or the background availability pass
/// finishes. The body leads with a Discord-friendly "content" string and carries the structured fields
/// alongside, so a Discord webhook renders the text and a generic receiver gets the data. No-op when no
/// URL is configured; failures are logged, never thrown, so a bad webhook cannot disrupt a scan.
/// </summary>
public sealed class WebhookNotifier
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServerApplicationHost _appHost;
    private readonly ILogger<WebhookNotifier> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookNotifier"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="appHost">The server host, for identifying which Jellyfin instance fired the webhook.</param>
    /// <param name="logger">The logger.</param>
    public WebhookNotifier(IHttpClientFactory httpClientFactory, IServerApplicationHost appHost, ILogger<WebhookNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _appHost = appHost;
        _logger = logger;
    }

    /// <summary>
    /// Posts an event to the configured webhook, if one is set.
    /// </summary>
    /// <param name="eventName">The event name (for example "scan" or "availability").</param>
    /// <param name="content">A short human-readable summary line.</param>
    /// <param name="fields">Structured fields to include in the payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the post finishes (or is skipped).</returns>
    public async Task NotifyAsync(string eventName, string content, IReadOnlyDictionary<string, object?> fields, CancellationToken cancellationToken)
    {
        var url = Plugin.RequireConfiguration().WebhookUrl;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        // Only post over http/https. The URL is admin-configured outbound, so we do not block private or
        // loopback hosts (a LAN webhook sink is a normal self-hosted setup), but we do refuse other schemes
        // (file, ftp, ...) that a typo or paste could otherwise turn into something unexpected.
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
        {
            _logger.LogWarning("Webhook skipped: URL scheme '{Scheme}' is not http or https", uri.Scheme);
            return;
        }

        try
        {
            // Identify which Jellyfin instance fired this, so one shared webhook URL can serve several.
            var serverName = string.IsNullOrWhiteSpace(_appHost.FriendlyName) ? "Jellyfin" : _appHost.FriendlyName;
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["content"] = "[" + serverName + "] " + content,
                ["event"] = eventName,
                ["server"] = serverName,
                ["serverId"] = _appHost.SystemId
            };
            foreach (var field in fields)
            {
                payload[field.Key] = field.Value;
            }

            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            using var body = new StringContent(json, Encoding.UTF8, "application/json");
            var client = _httpClientFactory.CreateClient(NamedClient.Default);

            // A Discord or Slack webhook carries its secret token in the URL path, so log only the host.
            var safeUrl = uri.GetLeftPart(UriPartial.Authority);
            if (Plugin.DetailedApiLogging)
            {
                _logger.LogDebug("Webhook: POST {Url} body {Body}", safeUrl, json);
            }

            using var response = await client.PostAsync(uri, body, cancellationToken).ConfigureAwait(false);
            if (Plugin.DetailedApiLogging)
            {
                _logger.LogDebug("Webhook: POST {Url} returned {Status}", safeUrl, (int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Webhook returned {Status} for event '{Event}'", (int)response.StatusCode, eventName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook POST failed for event '{Event}'", eventName);
        }
    }
}
