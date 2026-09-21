using System.Text;
using Jellyfin.Plugin.MindTheGaps.Services.Images;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ImageSnifferTests
{
    [Fact]
    public void ContentType_NamesEachRasterFormatByItsFirstBytes()
    {
        Assert.Equal("image/jpeg", ImageSniffer.ContentType(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.Equal("image/png", ImageSniffer.ContentType(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }));
        Assert.Equal("image/gif", ImageSniffer.ContentType(Encoding.ASCII.GetBytes("GIF89a\0\0\0\0\0\0")));
        Assert.Equal("image/gif", ImageSniffer.ContentType(Encoding.ASCII.GetBytes("GIF87a\0\0\0\0\0\0")));
        Assert.Equal("image/webp", ImageSniffer.ContentType(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBP")));
        Assert.Equal("image/avif", ImageSniffer.ContentType(Encoding.ASCII.GetBytes("\0\0\0\0ftypavif")));
    }

    [Fact]
    public void ContentType_RefusesWhatIsNotARasterImage()
    {
        Assert.Null(ImageSniffer.ContentType(Encoding.ASCII.GetBytes("<svg xmlns=\"h")));
        Assert.Null(ImageSniffer.ContentType(Encoding.ASCII.GetBytes("<!DOCTYPE htm")));
        Assert.Null(ImageSniffer.ContentType(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVE")));
        Assert.Null(ImageSniffer.ContentType(Encoding.ASCII.GetBytes("\0\0\0\0ftypmp42")));
        Assert.Null(ImageSniffer.ContentType([]));
        Assert.Null(ImageSniffer.ContentType(new byte[] { 0xFF }));
    }
}
