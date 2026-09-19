using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// A file compiled into the plugin, with the validators to serve it as one: an ETag that is its content hash
/// and a last-modified time from the assembly it shipped in. The ETag is the content, not the plugin version,
/// so two builds of the same version (a dev loop, a hotfix) cannot leave a browser holding the older copy
/// on a 304. The host serves plugin resources without any of this, which is why the plugin proxies them.
/// </summary>
internal sealed class EmbeddedAsset
{
    private EmbeddedAsset(byte[] bytes, string contentType, string hash, DateTimeOffset? lastModified)
    {
        Bytes = bytes;
        ContentType = contentType;
        Hash = hash;
        ETag = new EntityTagHeaderValue(string.Concat("\"", hash, "\""));
        LastModified = lastModified;
    }

    /// <summary>
    /// Gets the file's bytes.
    /// </summary>
    public byte[] Bytes { get; }

    /// <summary>
    /// Gets the media type to serve it as.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Gets the SHA-256 of the content, lowercase hex. A URL that carries it can never name two different files.
    /// </summary>
    public string Hash { get; }

    /// <summary>
    /// Gets the entity tag, the quoted <see cref="Hash"/>.
    /// </summary>
    public EntityTagHeaderValue ETag { get; }

    /// <summary>
    /// Gets when the assembly holding the file was written, or <see langword="null"/> when it has no file on disk.
    /// </summary>
    public DateTimeOffset? LastModified { get; }

    /// <summary>
    /// Reads an embedded resource, or returns <see langword="null"/> when the assembly has none by that name.
    /// </summary>
    /// <param name="assembly">The assembly holding the resource.</param>
    /// <param name="resourceName">The full manifest resource name.</param>
    /// <param name="contentType">The media type to serve it as.</param>
    /// <returns>The asset, or <see langword="null"/>.</returns>
    public static EmbeddedAsset? Load(Assembly assembly, string resourceName, string contentType)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        return new EmbeddedAsset(bytes, contentType, Convert.ToHexStringLower(SHA256.HashData(bytes)), WriteTime(assembly));
    }

    /// <summary>
    /// Gets when an assembly's file was last written, which is when anything compiled into it last changed.
    /// </summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>The time, at HTTP-date resolution, or <see langword="null"/> when the assembly has no file on disk.</returns>
    public static DateTimeOffset? WriteTime(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return !string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location)
            ? ConditionalGet.ToHttpDate(new DateTimeOffset(File.GetLastWriteTimeUtc(assembly.Location), TimeSpan.Zero))
            : null;
    }
}
