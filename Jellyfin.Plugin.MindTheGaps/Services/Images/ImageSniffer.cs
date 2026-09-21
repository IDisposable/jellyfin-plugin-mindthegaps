using System;

namespace Jellyfin.Plugin.MindTheGaps.Services.Images;

/// <summary>
/// Names an image by its first bytes rather than by what the upstream host claimed, so nothing but a raster
/// image is ever stored or served. SVG is left out on purpose: served from the server's own origin it could
/// run script.
/// </summary>
internal static class ImageSniffer
{
    /// <summary>
    /// The most leading bytes <see cref="ContentType"/> looks at.
    /// </summary>
    public const int HeadLength = 12;

    /// <summary>
    /// Gets the content type of the image the bytes start.
    /// </summary>
    /// <param name="head">The first bytes of the file.</param>
    /// <returns>The content type, or <see langword="null"/> when it is not a JPEG, PNG, GIF, WebP or AVIF.</returns>
    public static string? ContentType(ReadOnlySpan<byte> head)
    {
        ReadOnlySpan<byte> jpeg = [0xFF, 0xD8, 0xFF];
        if (head.StartsWith(jpeg))
        {
            return "image/jpeg";
        }

        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (head.StartsWith(png))
        {
            return "image/png";
        }

        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (head.Length >= HeadLength && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (head.Length >= HeadLength && head.Slice(4, 4).SequenceEqual("ftyp"u8)
            && (head.Slice(8, 4).SequenceEqual("avif"u8) || head.Slice(8, 4).SequenceEqual("avis"u8)))
        {
            return "image/avif";
        }

        return null;
    }
}
