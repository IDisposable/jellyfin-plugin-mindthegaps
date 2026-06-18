using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using MediaBrowser.Common.Net;
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
    private readonly ILogger<WebhookNotifier> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookNotifier"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public WebhookNotifier(IHttpClientFactory httpClientFactory, ILogger<WebhookNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
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
        var url = (Plugin.Instance?.Configuration ?? new PluginConfiguration()).WebhookUrl;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        try
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["content"] = content,
                ["event"] = eventName
            };
            foreach (var field in fields)
            {
                payload[field.Key] = field.Value;
            }

            using var body = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            using var response = await client.PostAsync(uri, body, cancellationToken).ConfigureAwait(false);
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
