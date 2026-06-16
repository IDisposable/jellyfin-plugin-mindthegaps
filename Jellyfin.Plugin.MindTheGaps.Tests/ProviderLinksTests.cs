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
