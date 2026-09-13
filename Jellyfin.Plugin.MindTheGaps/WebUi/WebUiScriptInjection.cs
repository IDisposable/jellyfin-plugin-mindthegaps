using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Adds the web UI client script to jellyfin-web's index.html as it is served. Jellyfin has no hook for
/// a plugin to extend the web client, and index.html on disk is root-owned in a container and replaced on
/// every upgrade, so the tag is added at request time instead: an <see cref="IStartupFilter"/> puts this
/// middleware ahead of the static-file handler, buffers only the index.html response, and rewrites it.
/// Defensive throughout: anything but a 200 text/html GET for index.html passes through untouched, the
/// feature toggle is read per request so switching it off needs no restart, and an error while rewriting
/// serves the original page.
/// </summary>
public sealed class WebUiScriptInjection : IStartupFilter
{
    private readonly ILogger<WebUiScriptInjection> _logger;
    private int _announced;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebUiScriptInjection"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public WebUiScriptInjection(ILogger<WebUiScriptInjection> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            // Outermost, ahead of the host's pipeline, so the request can be normalised (no compression, no
            // ranges) before the static-file handler sees it and the plain response can be rewritten.
            app.Use(InvokeAsync);
            next(app);
        };
    }

    /// <summary>
    /// Determines whether a request path is the web client's index.html, however it is addressed: an explicit
    /// <c>/web/index.html</c>, or the <c>/web</c> and <c>/web/</c> shell routes. Suffix-matched so a base URL
    /// prefix (<c>/jellyfin/web/</c>) still matches.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <returns><see langword="true"/> for index.html.</returns>
    public static bool IsIndexRequest(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web", StringComparison.OrdinalIgnoreCase);
    }

    private async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            || !IsIndexRequest(context.Request.Path.Value)
            || Plugin.Instance?.Configuration.WebUiEnabled != true)
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Ask for the plain, complete document: no compression to undo, and no 206 that would pass through
        // un-rewritten with a total length that no longer matches.
        context.Request.Headers.Remove(HeaderNames.AcceptEncoding);
        context.Request.Headers.Remove(HeaderNames.Range);
        context.Request.Headers.Remove(HeaderNames.IfRange);

        var original = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next().ConfigureAwait(false);
        }
        catch
        {
            // Nothing has reached the client yet; let the host's error handling answer rather than flushing
            // a truncated 200.
            context.Response.Body = original;
            throw;
        }

        context.Response.Body = original;
        buffer.Seek(0, SeekOrigin.Begin);

        var isHtml = context.Response.StatusCode == StatusCodes.Status200OK
            && (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) ?? false);
        if (!isHtml)
        {
            await buffer.CopyToAsync(original, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        string html;
        using (var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
        {
            html = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        }

        try
        {
            var version = typeof(WebUiScriptInjection).Assembly.GetName().Version?.ToString() ?? "0";
            var injected = IndexHtmlInjector.Inject(html, version);
            if (!ReferenceEquals(injected, html) && Interlocked.Exchange(ref _announced, 1) == 0)
            {
                _logger.LogInformation("Web UI: client script added to index.html at request time.");
            }

            html = injected;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web UI: could not add the client script to index.html; serving it unchanged.");
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength = bytes.Length;

        // The body changed, so the static handler's validators no longer describe it.
        context.Response.Headers.Remove(HeaderNames.ETag);
        context.Response.Headers.Remove(HeaderNames.LastModified);
        context.Response.Headers.Remove(HeaderNames.AcceptRanges);
        await original.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }
}
