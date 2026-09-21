using System;

namespace Jellyfin.Plugin.MindTheGaps.Services.Images;

/// <summary>
/// One image held in the cache, ready to be served.
/// </summary>
public sealed class CachedImage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CachedImage"/> class.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="contentType">The image's content type.</param>
    /// <param name="etag">The quoted entity tag, which names the address the image came from and when it was
    /// fetched.</param>
    /// <param name="lastModified">When the file was last written.</param>
    public CachedImage(string path, string contentType, string etag, DateTimeOffset lastModified)
    {
        Path = path;
        ContentType = contentType;
        ETag = etag;
        LastModified = lastModified;
    }

    /// <summary>
    /// Gets the file.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the image's content type.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Gets the quoted entity tag.
    /// </summary>
    public string ETag { get; }

    /// <summary>
    /// Gets when the file was last written.
    /// </summary>
    public DateTimeOffset LastModified { get; }
}
