using System;
using Jellyfin.Plugin.MindTheGaps.Services.Images;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ImageHostsTests
{
    [Theory]
    [InlineData("https://image.tmdb.org/t/p/w500/abc.jpg")]
    [InlineData("https://coverartarchive.org/release-group/1/front")]
    [InlineData("https://ia800000.us.archive.org/1/items/mbid-1/front.jpg")]
    [InlineData("https://covers.openlibrary.org/b/id/1-M.jpg")]
    [InlineData("https://images.justwatch.com/poster/1/s166/x.webp")]
    [InlineData("https://m.media-amazon.com/images/M/x.jpg")]
    [InlineData("https://i.discogs.com/x.jpeg")]
    [InlineData("https://artworks.thetvdb.com/banners/posters/1.jpg")]
    [InlineData("https://static.tvmaze.com/uploads/images/medium_portrait/1.jpg")]
    [InlineData("HTTPS://IMAGE.TMDB.ORG/t/p/w500/abc.jpg")]
    public void IsAllowed_AcceptsTheProvidersTheSourcesUse(string address)
    {
        Assert.True(ImageHosts.IsAllowed(new Uri(address)));
    }

    [Theory]
    [InlineData("http://image.tmdb.org/t/p/w500/abc.jpg")]
    [InlineData("https://example.com/a.jpg")]
    [InlineData("https://evil-tmdb.org/a.jpg")]
    [InlineData("https://tmdb.org.evil.example/a.jpg")]
    [InlineData("https://image.tmdb.org:8443/a.jpg")]
    [InlineData("https://user:secret@image.tmdb.org/a.jpg")]
    [InlineData("https://localhost/a.jpg")]
    [InlineData("https://127.0.0.1/a.jpg")]
    [InlineData("ftp://image.tmdb.org/a.jpg")]
    public void IsAllowed_RefusesEverythingElse(string address)
    {
        Assert.False(ImageHosts.IsAllowed(new Uri(address)));
    }

    [Fact]
    public void IsAllowed_AcceptsTheHostsTheClientsBuildTheirImageAddressesOn()
    {
        Assert.True(ImageHosts.IsAllowed(new Uri(Services.Tmdb.TmdbClient.BuildLogoUrl("/logo.jpg")!)));
        Assert.True(ImageHosts.IsAllowed(new Uri(Services.MusicBrainz.MusicBrainzClient.CoverArtUrl("00000000-0000-0000-0000-000000000000")!)));
        Assert.True(ImageHosts.IsAllowed(new Uri(Services.OpenLibrary.OpenLibraryClient.CoverUrl(1)!)));
        Assert.True(ImageHosts.IsAllowed(new Uri(Services.JustWatch.JustWatchClient.PosterUrl("/poster/1/{profile}/x.{format}")!)));
    }
}
