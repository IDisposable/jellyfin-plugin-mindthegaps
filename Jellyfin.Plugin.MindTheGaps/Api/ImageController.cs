using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Images;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// Serves the images the plugin's pages show from the server's own cache. Anonymous, because an image tag
/// cannot send the auth header, which is why it only ever fetches from the hosts <see cref="ImageHosts"/>
/// lists and keeps only what is a raster image under a size limit. Nothing in it is specific to a user or to
/// the library: they are the posters and covers any browser would load from the providers.
/// Whenever the cache cannot serve an image (it is off, not held and not fetchable at once, or gone from the
/// folder since it was found) the answer is a temporary redirect to the provider's own address, so a host that
/// refuses the server still shows its images to a browser that can reach it.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("MindTheGaps/Image")]
public class ImageController : ControllerBase
{
    private const int MaxAddressLength = 2048;
    private readonly ImageCache _images;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageController"/> class.
    /// </summary>
    /// <param name="images">The image cache.</param>
    public ImageController(ImageCache images)
    {
        _images = images;
    }

    /// <summary>
    /// Gets an image.
    /// </summary>
    /// <param name="u">The address of the image at the provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The image; a temporary redirect to the provider when the cache is off or cannot serve it; or 404
    /// for an address that is not an allowed host's.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromQuery] string? u, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(u)
            || u.Length > MaxAddressLength
            || !Uri.TryCreate(u, UriKind.Absolute, out var uri)
            || !ImageHosts.IsAllowed(uri))
        {
            return NotFound();
        }

        var config = Plugin.RequireConfiguration();
        return await Serve(uri, config.ImageCacheEnabled, ImageCache.CapBytes(config), cancellationToken).ConfigureAwait(false);
    }

    // The part that does not read the plugin's configuration, so it is tested without a running server.
    internal async Task<IActionResult> Serve(Uri uri, bool cacheEnabled, long capBytes, CancellationToken cancellationToken)
    {
        if (!cacheEnabled)
        {
            return ToProvider(uri, TimeSpan.FromHours(1));
        }

        var image = await _images.GetAsync(uri, capBytes, cancellationToken).ConfigureAwait(false);
        var stream = image is null ? null : OpenOrNull(image.Path);
        if (image is null || stream is null)
        {
            // Short, since the next request may find the image cached, or the host answering again.
            return ToProvider(uri, TimeSpan.FromMinutes(1));
        }

        Response.Headers[HeaderNames.CacheControl] = "public, max-age=604800";
        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        return File(stream, image.ContentType, image.LastModified, new EntityTagHeaderValue(image.ETag));
    }

    // The file, or null when it has gone since it was found: the trim deleted it, or the server's own sweep did.
    private static FileStream? OpenOrNull(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private IActionResult ToProvider(Uri uri, TimeSpan browserMayKeepIt)
    {
        Response.Headers[HeaderNames.CacheControl] = "public, max-age=" + (int)browserMayKeepIt.TotalSeconds;
        return Redirect(uri.AbsoluteUri);
    }
}
