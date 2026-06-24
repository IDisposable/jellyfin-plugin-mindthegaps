using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Exercises the shared retry/backoff helper with an in-memory handler (no network), so the retry, the
// give-up, and the cancellation behavior are pinned down. Backoff is real but short (sub-second), so a
// retrying case adds only a moment to the run. Shares the non-parallel collection with ServiceCircuitTests
// because HttpRetry feeds the process-wide circuit; resetting it per test keeps the cases independent.
[Collection("ServiceCircuit")]
public class HttpRetryTests
{
    public HttpRetryTests()
    {
        ServiceCircuit.ResetAll();
        ServiceCircuit.OnTrip = null;
    }

    private static async Task<HttpResponseMessage> SendAsync(StubHandler handler, CancellationToken ct = default)
    {
        using var client = new HttpClient(handler);
        return await HttpRetry.SendAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Get, "https://example.test/x"),
            NullLogger.Instance,
            "Test",
            "/x",
            ct);
    }

    [Fact]
    public async Task RetriesA503ThenReturnsTheSuccess()
    {
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var response = await SendAsync(handler);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task DoesNotRetryANonRetryableStatus()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, HttpStatusCode.OK);
        using var response = await SendAsync(handler);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GivesUpAfterMaxAttemptsAndReturnsTheLastFailure()
    {
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var response = await SendAsync(handler);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, handler.Calls); // three tries total, then stop (the fourth OK is never reached)
    }

    [Fact]
    public async Task HonorsRetryAfterLongerThanTheCapByGivingUpWithoutWaiting()
    {
        // A 999s Retry-After exceeds the 30s cap, so the helper returns the failure immediately rather than
        // blocking a scan; one call, no second attempt.
        var handler = new StubHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK)
        {
            RetryAfterSeconds = 999
        };
        using var response = await SendAsync(handler);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task PropagatesCallerCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubHandler(HttpStatusCode.OK);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(handler, cts.Token));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses;

        public StubHandler(params HttpStatusCode[] statuses)
        {
            _statuses = new Queue<HttpStatusCode>(statuses);
        }

        public int Calls { get; private set; }

        public int? RetryAfterSeconds { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            var status = _statuses.Count > 0 ? _statuses.Dequeue() : HttpStatusCode.OK;
            var response = new HttpResponseMessage(status);
            if (RetryAfterSeconds is { } seconds)
            {
                response.Headers.Add("Retry-After", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return Task.FromResult(response);
        }
    }
}
