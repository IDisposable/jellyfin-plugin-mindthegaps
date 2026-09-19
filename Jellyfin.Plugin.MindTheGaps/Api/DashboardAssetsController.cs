using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// Serves the dashboard pages' script and stylesheet as files of their own, so a browser keeps them instead
/// of downloading them inside every load of the page. The host serves a plugin page's HTML with no validators
/// at all, so code carried inline in it is downloaded on every load.
/// Anonymous, because a script tag cannot send the auth header, and these are the same files that ship in the
/// plugin, with nothing about the server or its library in them.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("MindTheGaps/Dashboard")]
public class DashboardAssetsController : ControllerBase
{
    /// <summary>
    /// The number of leading hash characters the page's URLs carry, and the fewest that make a URL count as
    /// naming a specific version of the file.
    /// </summary>
    internal const int VersionLength = 16;

    private const string Prefix = "Jellyfin.Plugin.MindTheGaps.Web.";

    private static readonly Dictionary<string, Lazy<EmbeddedAsset?>> _assets = new(StringComparer.Ordinal)
    {
        ["mindthegaps.css"] = Asset("mindthegaps.css", "text/css"),
        ["report.js"] = Asset("mindthegaps.report.bundle.js", "application/javascript"),
        ["settings.js"] = Asset("mindthegaps.settings.bundle.js", "application/javascript")
    };

    /// <summary>
    /// Gets one of the dashboard's files.
    /// </summary>
    /// <param name="name">The file: <c>mindthegaps.css</c>, <c>report.js</c> or <c>settings.js</c>.</param>
    /// <param name="v">The content hash the page was built with. A URL that carries the current one names this exact
    /// content forever, so it is cached for a year; anything else is revalidated, so a stale page cannot pin
    /// newer content under an old address.</param>
    /// <returns>The file, or 404 for a name that is not one of them.</returns>
    [HttpGet("{name}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Get(string name, [FromQuery] string? v)
    {
        if (!_assets.TryGetValue(name, out var lazy) || lazy.Value is not { } asset)
        {
            return NotFound();
        }

        var addressed = v is { Length: >= VersionLength } && asset.Hash.StartsWith(v, StringComparison.Ordinal);
        Response.Headers[HeaderNames.CacheControl] = addressed ? "public, max-age=31536000, immutable" : "public, no-cache";
        return File(asset.Bytes, asset.ContentType, asset.LastModified, asset.ETag);
    }

    private static Lazy<EmbeddedAsset?> Asset(string resource, string contentType)
        => new(() => EmbeddedAsset.Load(typeof(DashboardAssetsController).Assembly, Prefix + resource, contentType));
}
