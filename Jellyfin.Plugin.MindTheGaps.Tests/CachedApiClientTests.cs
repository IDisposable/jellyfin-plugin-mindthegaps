using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Verifies the shared cached GET path: a repeated lookup is served from the cache (one network call), distinct
// URLs are cached apart, a failure is not cached, and the per-request configuration (auth headers) is applied.
// Shares the non-parallel collection with the other HTTP-infrastructure tests because it drives HttpRetry.
[Collection("ServiceCircuit")]
public class CachedApiClientTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public CachedApiClientTests()
    {
        ServiceCircuit.ResetAll();
        ServicePacer.ResetAll();
    }

    [Fact]
    public async Task GetJson_CachesSuccess_SoSecondCallSkipsTheNetwork()
    {
        var handler = new CountingHandler(_ => Json(HttpStatusCode.OK, "{\"name\":\"a\"}"));
        var client = Make(handler);

        var first = await client.GetJsonAsync<Payload>("Svc", "https://x.test/a", TimeSpan.FromMinutes(5), Options, null, default);
        var second = await client.GetJsonAsync<Payload>("Svc", "https://x.test/a", TimeSpan.FromMinutes(5), Options, null, default);

        Assert.Equal("a", first!.Name);
        Assert.Equal("a", second!.Name);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetJson_DistinctUrls_AreCachedSeparately()
    {
        var handler = new CountingHandler(req =>
            Json(HttpStatusCode.OK, req.RequestUri!.AbsolutePath.EndsWith('a') ? "{\"name\":\"a\"}" : "{\"name\":\"b\"}"));
        var client = Make(handler);

        var a = await client.GetJsonAsync<Payload>("Svc", "https://x.test/a", TimeSpan.FromMinutes(5), Options, null, default);
        var b = await client.GetJsonAsync<Payload>("Svc", "https://x.test/b", TimeSpan.FromMinutes(5), Options, null, default);

        Assert.Equal("a", a!.Name);
        Assert.Equal("b", b!.Name);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetJson_DoesNotCacheAFailure()
    {
        var statuses = new Queue<HttpStatusCode>(new[] { HttpStatusCode.InternalServerError, HttpStatusCode.OK });
        var handler = new CountingHandler(_ => Json(statuses.Dequeue(), "{\"name\":\"a\"}"));
        var client = Make(handler);

        var first = await client.GetJsonAsync<Payload>("Svc", "https://x.test/a", TimeSpan.FromMinutes(5), Options, null, default);
        var second = await client.GetJsonAsync<Payload>("Svc", "https://x.test/a", TimeSpan.FromMinutes(5), Options, null, default);

        Assert.Null(first);
        Assert.Equal("a", second!.Name);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetJson_AppliesConfigureRequest()
    {
        var sawHeader = false;
        var handler = new CountingHandler(req =>
        {
            sawHeader = req.Headers.Contains("X-Test");
            return Json(HttpStatusCode.OK, "{\"name\":\"a\"}");
        });
        var client = Make(handler);

        await client.GetJsonAsync<Payload>("Svc", "https://x.test/a", TimeSpan.FromMinutes(5), Options, r => r.Headers.Add("X-Test", "1"), default);

        Assert.True(sawHeader);
    }

    private static CachedApiClient Make(CountingHandler handler)
        => new(new StubFactory(handler), new MemoryCache(new MemoryCacheOptions()), NullLogger<CachedApiClient>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record Payload(string Name);

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_respond(request));
        }
    }
}
