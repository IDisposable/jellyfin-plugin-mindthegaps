using System;
using Jellyfin.Plugin.MindTheGaps.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ConditionalGetTests
{
    private static readonly DateTime _changed = new(2026, 3, 4, 5, 6, 7, 890, DateTimeKind.Utc);

    private static DefaultHttpContext Context(string? ifNoneMatch = null, DateTimeOffset? ifModifiedSince = null)
    {
        var context = new DefaultHttpContext();
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

    [Fact]
    public void ARequestWithNoValidators_IsServedAndStamped()
    {
        var context = Context();

        Assert.False(ConditionalGet.IsNotModified(context.Request, context.Response, "abc.1", _changed));

        var headers = context.Response.GetTypedHeaders();
        Assert.Equal("W/\"abc.1\"", headers.ETag!.ToString());
        Assert.True(headers.CacheControl!.Private);
        Assert.True(headers.CacheControl.NoCache);
        Assert.False(headers.CacheControl.Public);
        Assert.NotNull(headers.LastModified);
    }

    [Fact]
    public void AMatchingTag_IsNotModified_WhetherTheClientSendsItWeakOrStrong()
    {
        foreach (var sent in new[] { "W/\"abc.1\"", "\"abc.1\"", "\"other\", W/\"abc.1\"", "*" })
        {
            var context = Context(ifNoneMatch: sent);
            Assert.True(ConditionalGet.IsNotModified(context.Request, context.Response, "abc.1", _changed), sent);
        }
    }

    [Fact]
    public void ADifferentTag_IsServed()
    {
        var context = Context(ifNoneMatch: "W/\"abc.0\"");

        Assert.False(ConditionalGet.IsNotModified(context.Request, context.Response, "abc.1", _changed));
    }

    [Fact]
    public void WithoutATag_AnUpToDateModifiedSinceIsNotModified_AndAnOlderOneIsServed()
    {
        // The header has whole seconds, and the server's timestamp has more.
        var current = Context(ifModifiedSince: new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var stale = Context(ifModifiedSince: new DateTimeOffset(2026, 3, 4, 5, 6, 6, TimeSpan.Zero));

        Assert.True(ConditionalGet.IsNotModified(current.Request, current.Response, "abc.1", _changed));
        Assert.False(ConditionalGet.IsNotModified(stale.Request, stale.Response, "abc.1", _changed));
    }

    [Fact]
    public void ATagTakesPrecedenceOverModifiedSince()
    {
        // The date says current, the tag says changed: the tag decides.
        var context = Context(ifNoneMatch: "W/\"abc.0\"", ifModifiedSince: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.False(ConditionalGet.IsNotModified(context.Request, context.Response, "abc.1", _changed));
    }

    [Fact]
    public void AnUnknownModifiedTime_OmitsTheHeaderAndNeverMatchesByDate()
    {
        var context = Context(ifModifiedSince: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.False(ConditionalGet.IsNotModified(context.Request, context.Response, "abc.1", DateTime.MinValue));
        Assert.Null(context.Response.GetTypedHeaders().LastModified);
    }
}
