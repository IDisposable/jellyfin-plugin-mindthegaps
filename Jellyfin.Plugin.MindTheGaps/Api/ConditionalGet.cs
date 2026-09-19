using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// Lets the browser revalidate a response it holds instead of downloading it again, and lets the server
/// answer without doing the work when nothing changed. Every response carries the validators, and the caller
/// returns 304 when this says the client's copy is current, before computing anything.
/// </summary>
internal static class ConditionalGet
{
    /// <summary>
    /// Stamps the response with its validators and reports whether the request's copy is still current.
    /// The cache directive is <c>private, no-cache</c>: these are authenticated, so a shared cache must never
    /// keep one, and <c>no-cache</c> makes the browser revalidate every time, which is a cheap 304.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="response">The response to stamp.</param>
    /// <param name="tag">What identifies the current content. Weak, since the compressed bytes differ from the uncompressed ones.</param>
    /// <param name="lastModifiedUtc">When the content last changed, or <see cref="DateTime.MinValue"/> when unknown.</param>
    /// <returns><see langword="true"/> when the caller should answer 304 Not Modified.</returns>
    public static bool IsNotModified(HttpRequest request, HttpResponse response, string tag, DateTime lastModifiedUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var etag = new EntityTagHeaderValue("\"" + tag + "\"", isWeak: true);
        var lastModified = lastModifiedUtc > DateTime.MinValue ? ToHttpDate(new DateTimeOffset(DateTime.SpecifyKind(lastModifiedUtc, DateTimeKind.Utc))) : (DateTimeOffset?)null;

        var responseHeaders = response.GetTypedHeaders();
        responseHeaders.CacheControl = new CacheControlHeaderValue { Private = true, NoCache = true };
        responseHeaders.ETag = etag;
        responseHeaders.LastModified = lastModified;

        var requestHeaders = request.GetTypedHeaders();
        return IsCurrent(requestHeaders.IfNoneMatch, requestHeaders.IfModifiedSince, etag, lastModified);
    }

    /// <summary>
    /// Decides whether a client's copy is still current. <c>If-None-Match</c> wins over <c>If-Modified-Since</c>
    /// when both are present (RFC 9110 section 13.1.3), and a date is only ever compared when there is no tag.
    /// </summary>
    /// <param name="ifNoneMatch">The request's <c>If-None-Match</c> tags.</param>
    /// <param name="ifModifiedSince">The request's <c>If-Modified-Since</c>, if any.</param>
    /// <param name="etag">The current tag.</param>
    /// <param name="lastModified">When the content last changed, at HTTP-date resolution (see <see cref="ToHttpDate"/>), or <see langword="null"/> when unknown.</param>
    /// <returns><see langword="true"/> when the client's copy matches.</returns>
    public static bool IsCurrent(IList<EntityTagHeaderValue> ifNoneMatch, DateTimeOffset? ifModifiedSince, EntityTagHeaderValue etag, DateTimeOffset? lastModified)
    {
        ArgumentNullException.ThrowIfNull(ifNoneMatch);
        ArgumentNullException.ThrowIfNull(etag);

        if (ifNoneMatch.Count > 0)
        {
            return ifNoneMatch.Any(t => t.Equals(EntityTagHeaderValue.Any) || t.Compare(etag, useStrongComparison: false));
        }

        return lastModified is { } modified && ifModifiedSince is { } since && modified <= since;
    }

    /// <summary>
    /// Truncates a time to whole seconds, which is all an HTTP date carries. Compared at that resolution, a
    /// copy that is current would otherwise look older than the server's sub-second timestamp.
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns>The time without its sub-second part.</returns>
    public static DateTimeOffset ToHttpDate(DateTimeOffset time) => time.AddTicks(-(time.UtcTicks % TimeSpan.TicksPerSecond));
}
