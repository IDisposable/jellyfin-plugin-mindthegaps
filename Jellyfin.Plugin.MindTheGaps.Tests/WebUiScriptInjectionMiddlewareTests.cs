using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The middleware serves index.html with the client script added, and gives that rewritten document validators
// of its own so a returning browser (or a CDN) revalidates it instead of downloading it again. The host's
// static-file handler stands behind it, so each test plays that handler with a fake next.
public class WebUiScriptInjectionMiddlewareTests
{
    private const string HostHtml = "<html><body><h1>jellyfin</h1></body></html>";
    private static readonly DateTimeOffset _hostModified = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static WebUiScriptInjection Middleware(bool enabled = true)
        => new(NullLogger<WebUiScriptInjection>.Instance, () => enabled);

    private static DefaultHttpContext Request(string path = "/web/index.html", string? ifNoneMatch = null, DateTimeOffset? ifModifiedSince = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (ifNoneMatch is not null)
        {
            context.Request.Headers[HeaderNames.IfNoneMatch] = ifNoneMatch;
        }

        if (ifModifiedSince is { } since)
        {
            context.Request.GetTypedHeaders().IfModifiedSince = since;
        }

        return context;
    }

    // The host's handler: the file's own tag and date, and the no-cache it sends for index.html.
    private static async Task Host(HttpContext context, string html = HostHtml, int status = StatusCodes.Status200OK, string contentType = "text/html")
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        context.Response.Headers[HeaderNames.ETag] = "\"host-tag\"";
        context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
        context.Response.GetTypedHeaders().LastModified = _hostModified;
        await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(html));
    }

    private static async Task<DefaultHttpContext> Serve(DefaultHttpContext context, string html = HostHtml, bool enabled = true)
    {
        await Middleware(enabled).InvokeAsync(context, () => Host(context, html));
        return context;
    }

    private static string Body(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private static string TagOf(HttpContext context) => context.Response.GetTypedHeaders().ETag!.ToString();

    [Fact]
    public async Task ServesTheDocumentWithTheScriptAndItsOwnValidators()
    {
        var context = await Serve(Request());

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("data-mtg-webui", Body(context), StringComparison.Ordinal);

        // A strong tag that is the hash of exactly the bytes served, not the host's tag for its file.
        var bytes = Encoding.UTF8.GetBytes(Body(context));
        Assert.Equal("\"" + Convert.ToHexStringLower(SHA256.HashData(bytes)) + "\"", TagOf(context));
        Assert.Equal(bytes.Length, context.Response.ContentLength);

        // The date is the later of the file's and the plugin's, so never earlier than the file's.
        Assert.True(context.Response.GetTypedHeaders().LastModified >= _hostModified);
        Assert.True(context.Response.GetTypedHeaders().CacheControl!.NoCache);
    }

    [Fact]
    public async Task AReturningClientWithTheCurrentTag_GetsA304WithNoBody()
    {
        var first = await Serve(Request());

        var second = await Serve(Request(ifNoneMatch: TagOf(first)));

        Assert.Equal(StatusCodes.Status304NotModified, second.Response.StatusCode);
        Assert.Empty(Body(second));
        Assert.Null(second.Response.ContentLength);
        Assert.False(second.Response.Headers.ContainsKey(HeaderNames.ContentType));
        Assert.Equal(TagOf(first), TagOf(second));
    }

    [Fact]
    public async Task TheClientsValidatorsAreNotHandedToTheHost()
    {
        var context = Request(ifNoneMatch: "\"anything\"", ifModifiedSince: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sawTag = true;
        var sawDate = true;

        await Middleware().InvokeAsync(context, () =>
        {
            sawTag = context.Request.Headers.ContainsKey(HeaderNames.IfNoneMatch);
            sawDate = context.Request.Headers.ContainsKey(HeaderNames.IfModifiedSince);
            return Host(context);
        });

        Assert.False(sawTag);
        Assert.False(sawDate);
    }

    [Fact]
    public async Task TheHostsOwnTag_IsNotMistakenForTheDocumentsTag()
    {
        var context = await Serve(Request(ifNoneMatch: "\"host-tag\""));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("data-mtg-webui", Body(context), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenTheHostsFileChanges_TheTagChanges_AndAnOldCopyIsReplaced()
    {
        var before = await Serve(Request());

        var after = await Serve(Request(ifNoneMatch: TagOf(before)), html: "<html><body><h1>jellyfin 2</h1></body></html>");

        Assert.Equal(StatusCodes.Status200OK, after.Response.StatusCode);
        Assert.NotEqual(TagOf(before), TagOf(after));
    }

    [Fact]
    public async Task WithoutATag_ADateAtOrAfterTheDocumentsIsCurrent_AndAnEarlierOneIsNot()
    {
        var served = await Serve(Request());
        var modified = served.Response.GetTypedHeaders().LastModified!.Value;

        var current = await Serve(Request(ifModifiedSince: modified));
        var stale = await Serve(Request(ifModifiedSince: modified.AddSeconds(-1)));

        Assert.Equal(StatusCodes.Status304NotModified, current.Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, stale.Response.StatusCode);
    }

    [Fact]
    public async Task ATagTakesPrecedenceOverADate()
    {
        var context = await Serve(Request(ifNoneMatch: "\"stale\"", ifModifiedSince: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task WhenSwitchedOff_TheHostsResponseIsUntouched_WithTheHostsValidators()
    {
        var context = Request(ifNoneMatch: "\"host-tag\"");
        var sawTag = false;

        await Middleware(enabled: false).InvokeAsync(context, () =>
        {
            sawTag = context.Request.Headers.ContainsKey(HeaderNames.IfNoneMatch);
            return Host(context);
        });

        Assert.True(sawTag);
        Assert.Equal(HostHtml, Body(context));
        Assert.Equal("\"host-tag\"", TagOf(context));
    }

    [Theory]
    [InlineData(StatusCodes.Status404NotFound, "text/html")]
    [InlineData(StatusCodes.Status200OK, "application/json")]
    public async Task ANonHtmlOrNon200Response_PassesThroughWithTheHostsHeaders(int status, string contentType)
    {
        var context = Request();

        await Middleware().InvokeAsync(context, () => Host(context, "not the page", status, contentType));

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("not the page", Body(context));
        Assert.Equal("\"host-tag\"", TagOf(context));
    }

    [Fact]
    public async Task ARequestForSomethingElse_IsNotTouched()
    {
        var context = Request(path: "/web/main.bundle.js", ifNoneMatch: "\"host-tag\"");
        var sawTag = false;

        await Middleware().InvokeAsync(context, () =>
        {
            sawTag = context.Request.Headers.ContainsKey(HeaderNames.IfNoneMatch);
            return Host(context, "console.log(1)", contentType: "application/javascript");
        });

        Assert.True(sawTag);
        Assert.Equal("console.log(1)", Body(context));
    }
}
