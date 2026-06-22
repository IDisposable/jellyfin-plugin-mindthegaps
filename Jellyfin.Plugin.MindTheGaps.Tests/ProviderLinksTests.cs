using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ProviderLinksTests
{
    [Fact]
    public void Build_Tmdb_Movie_ProducesMovieUrl()
    {
        var links = ProviderLinks.Build(BaseItemKind.Movie, new Dictionary<string, string> { ["Tmdb"] = "603" });
        var link = Assert.Single(links);
        Assert.Equal("TMDB", link.Name);
        Assert.Equal("https://www.themoviedb.org/movie/603", link.Url);
    }

    [Fact]
    public void Build_Tmdb_Series_ProducesTvUrl()
    {
        var links = ProviderLinks.Build(BaseItemKind.Series, new Dictionary<string, string> { ["Tmdb"] = "1399" });
        Assert.Equal("https://www.themoviedb.org/tv/1399", Assert.Single(links).Url);
    }

    [Fact]
    public void Build_Imdb_ProducesTitleUrl()
    {
        var links = ProviderLinks.Build(BaseItemKind.Movie, new Dictionary<string, string> { ["Imdb"] = "tt0133093" });
        Assert.Equal("https://www.imdb.com/title/tt0133093/", Assert.Single(links).Url);
    }

    [Fact]
    public void Build_JustWatch_FullPath_PrependsDomain()
    {
        var links = ProviderLinks.Build(BaseItemKind.Movie, new Dictionary<string, string> { ["JustWatch"] = "/us/movie/the-matrix" });
        Assert.Equal("https://www.justwatch.com/us/movie/the-matrix", Assert.Single(links).Url);
    }

    [Fact]
    public void Build_IsCaseInsensitiveOnProviderKey()
    {
        var links = ProviderLinks.Build(BaseItemKind.Movie, new Dictionary<string, string> { ["tmdb"] = "603" });
        Assert.Single(links);
    }

    [Fact]
    public void Build_UnknownProvider_IsSkipped()
    {
        var links = ProviderLinks.Build(BaseItemKind.Movie, new Dictionary<string, string> { ["Foo"] = "bar" });
        Assert.Empty(links);
    }

    [Fact]
    public void Build_EmptyValue_IsSkipped()
    {
        var links = ProviderLinks.Build(BaseItemKind.Movie, new Dictionary<string, string> { ["Tmdb"] = string.Empty });
        Assert.Empty(links);
    }

    [Fact]
    public void Build_Tvdb_Movie_UsesMovieDereferrer()
    {
        var links = ProviderLinks.Build(BaseItemKind.Movie, new Dictionary<string, string> { ["Tvdb"] = "12345" });
        Assert.Equal("https://thetvdb.com/dereferrer/movie/12345", Assert.Single(links).Url);
    }

    [Fact]
    public void Build_Tvdb_Series_UsesSeriesDereferrer()
    {
        var links = ProviderLinks.Build(BaseItemKind.Series, new Dictionary<string, string> { ["Tvdb"] = "12345" });
        Assert.Equal("https://thetvdb.com/dereferrer/series/12345", Assert.Single(links).Url);
    }

    [Fact]
    public void Build_Tvdb_Episode_UsesEpisodeDereferrer()
    {
        // An episode gap carries the episode's own TheTVDB id; "series/{id}" would 404.
        var links = ProviderLinks.Build(BaseItemKind.Episode, new Dictionary<string, string> { ["Tvdb"] = "8343820" });
        Assert.Equal("https://thetvdb.com/dereferrer/episode/8343820", Assert.Single(links).Url);
    }

    [Fact]
    public void Build_Tvdb_Season_UsesSeasonDereferrer()
    {
        var links = ProviderLinks.Build(BaseItemKind.Season, new Dictionary<string, string> { ["Tvdb"] = "555" });
        Assert.Equal("https://thetvdb.com/dereferrer/season/555", Assert.Single(links).Url);
    }

    [Fact]
    public void Build_Imdb_Episode_StaysTitleLink()
    {
        // Episode IMDb ids are valid title ids, so the episode keeps a direct title link.
        var links = ProviderLinks.Build(BaseItemKind.Episode, new Dictionary<string, string> { ["Imdb"] = "tt9310328" });
        Assert.Equal("https://www.imdb.com/title/tt9310328/", Assert.Single(links).Url);
    }

    [Fact]
    public void Build_Tmdb_Episode_IsSkipped()
    {
        // An episode's TMDB id cannot form a clean title URL, so no TMDB link is emitted.
        var links = ProviderLinks.Build(BaseItemKind.Episode, new Dictionary<string, string> { ["Tmdb"] = "123" });
        Assert.Empty(links);
    }

    [Fact]
    public void Build_MusicBrainzReleaseGroup_ProducesReleaseGroupUrl()
    {
        var links = ProviderLinks.Build(BaseItemKind.MusicAlbum, new Dictionary<string, string> { ["MusicBrainzReleaseGroup"] = "f4a31f0a" });
        var link = Assert.Single(links);
        Assert.Equal("MusicBrainz", link.Name);
        Assert.Equal("https://musicbrainz.org/release-group/f4a31f0a", link.Url);
    }

    [Fact]
    public void Build_Discogs_ProducesReleaseUrl()
    {
        var links = ProviderLinks.Build(BaseItemKind.MusicAlbum, new Dictionary<string, string> { ["Discogs"] = "100" });
        var link = Assert.Single(links);
        Assert.Equal("Discogs", link.Name);
        Assert.Equal("https://www.discogs.com/release/100", link.Url);
    }

    [Fact]
    public void Build_OpenLibrary_ProducesWorksUrl()
    {
        var links = ProviderLinks.Build(BaseItemKind.Book, new Dictionary<string, string> { ["OpenLibrary"] = "OL45804W" });
        var link = Assert.Single(links);
        Assert.Equal("OpenLibrary", link.Name);
        Assert.Equal("https://openlibrary.org/works/OL45804W", link.Url);
    }

    [Fact]
    public void Build_MultipleProviders_ProducesEach()
    {
        var links = ProviderLinks.Build(
            BaseItemKind.Movie,
            new Dictionary<string, string> { ["Tmdb"] = "603", ["Imdb"] = "tt0133093" });
        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.Name == "TMDB");
        Assert.Contains(links, l => l.Name == "IMDb");
    }
}
