using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// A stand-in for the image providers: answers every request with what the test says, counts them, and can hold
// each answer until a gate is opened. It is its own client factory, so it can be handed to the cache directly.
internal sealed class FakeImageHost : HttpMessageHandler, IHttpClientFactory
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    private readonly Task? _gate;
    private int _calls;

    public FakeImageHost(Func<HttpRequestMessage, HttpResponseMessage> respond, Task? gate = null)
    {
        _respond = respond;
        _gate = gate;
    }

    public int Calls => Volatile.Read(ref _calls);

    public static byte[] Jpeg(int length)
    {
        var bytes = new byte[length];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        return bytes;
    }

    public static HttpResponseMessage Ok(byte[] body)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (_gate is not null)
        {
            await _gate.ConfigureAwait(false);
        }

        var response = _respond(request);
        response.RequestMessage ??= request;
        return response;
    }
}
