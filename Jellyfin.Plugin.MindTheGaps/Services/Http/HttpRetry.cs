using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Http;

/// <summary>
/// Sends an HTTP request with the small retry/backoff policy shared by the plugin's hand-rolled service
/// clients (Trakt, TVmaze, TheTVDB, MusicBrainz, OpenLibrary, and the TMDB availability fetch). It retries
/// the "slow down / try again" statuses (429, 502, 503, 504) and transient connection failures, honouring
/// a <c>Retry-After</c> header when the server sends one and otherwise backing off exponentially. The wait
/// is capped so a background scan never stalls for long: if a call still fails, the last response is
/// returned (so the caller's existing non-success handling runs) or, for a transient exception, a plain
/// <see cref="HttpRequestException"/> is thrown so the caller treats it as an error rather than as
/// cancellation. Real caller cancellation always propagates immediately.
/// </summary>
internal static class HttpRetry
{
    // Three tries total (two retries): enough to ride out a brief rate-limit or blip without hammering.
    private const int MaxAttempts = 3;

    // Never block a scan longer than this for one call, even if the server's Retry-After asks for more.
    private static readonly TimeSpan _maxDelay = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan _baseDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Sends a request built fresh per attempt (a request message cannot be re-sent), retrying transient
    /// failures and rate-limit/unavailable statuses.
    /// </summary>
    /// <param name="client">The HTTP client to send on.</param>
    /// <param name="requestFactory">Builds the request; called once per attempt.</param>
    /// <param name="logger">The caller's logger, for retry diagnostics.</param>
    /// <param name="service">The service name, for log messages (for example "Trakt").</param>
    /// <param name="path">The request path, for log messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The final response; the caller owns and disposes it.</returns>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        ILogger logger,
        string service,
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = requestFactory();
                response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller asked us to stop; let cancellation propagate.
                throw;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                if (attempt >= MaxAttempts)
                {
                    // Out of retries. Surface as a non-cancellation failure (even a request timeout, which is
                    // an OperationCanceledException) so the caller treats it as an error and returns its
                    // default, rather than mistaking it for cancellation and aborting the whole scan.
                    throw new HttpRequestException(
                        string.Create(CultureInfo.InvariantCulture, $"{service} GET {path} failed after {MaxAttempts} attempts"),
                        ex);
                }

                var delay = Backoff(attempt);
                logger.LogWarning(ex, "{Service} GET {Path}: transient error, retrying in {Delay:n1}s (attempt {Attempt}/{Max})", service, path, delay.TotalSeconds, attempt, MaxAttempts);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (attempt < MaxAttempts && IsRetryableStatus(response.StatusCode))
            {
                var delay = RetryAfter(response) ?? Backoff(attempt);
                if (delay <= _maxDelay)
                {
                    logger.LogWarning("{Service} GET {Path} returned {Status}, retrying in {Delay:n1}s (attempt {Attempt}/{Max})", service, path, (int)response.StatusCode, delay.TotalSeconds, attempt, MaxAttempts);
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // The server asked us to wait longer than we will block a scan for; stop and let the caller
                // handle this run's failure. The next scheduled run retries.
                logger.LogWarning("{Service} GET {Path} returned {Status} with Retry-After {Delay:n0}s, longer than the {Cap:n0}s cap; giving up this run", service, path, (int)response.StatusCode, delay.TotalSeconds, _maxDelay.TotalSeconds);
            }

            return response;
        }
    }

    private static bool IsTransient(Exception ex)
        => ex is HttpRequestException
        || ex is IOException
        // A request timeout surfaces as OperationCanceledException; caller cancellation is handled separately.
        || ex is OperationCanceledException;

    private static bool IsRetryableStatus(HttpStatusCode status)
        => status == HttpStatusCode.TooManyRequests // 429
        || status == HttpStatusCode.BadGateway // 502
        || status == HttpStatusCode.ServiceUnavailable // 503
        || status == HttpStatusCode.GatewayTimeout; // 504

    private static TimeSpan Backoff(int attempt)
    {
        // 0.5s, 1s, 2s, ... capped. A little per-attempt jitter spreads a burst of parallel callers apart
        // without needing a random source (which the scan avoids for determinism).
        var ms = (_baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1)) + ((attempt * 53) % 100);
        var delay = TimeSpan.FromMilliseconds(ms);
        return delay > _maxDelay ? _maxDelay : delay;
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }
}
