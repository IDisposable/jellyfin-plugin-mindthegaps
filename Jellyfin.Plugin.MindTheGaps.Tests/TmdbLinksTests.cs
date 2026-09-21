using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class TmdbLinksTests
{
    [Theory]
    [InlineData("https://www.themoviedb.org/movie/949-heat/watch?locale=US", true)]
    [InlineData("https://themoviedb.org/tv/1399/watch", true)]
    [InlineData("http://www.themoviedb.org/movie/1/watch", false)]
    [InlineData("https://www.themoviedb.org.evil.example/watch", false)]
    [InlineData("https://notthemoviedb.org/watch", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/movie/1/watch", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsWatchUrl_AcceptsOnlyHttpsOnTmdbsOwnDomain(string? url, bool expected)
        => Assert.Equal(expected, TmdbLinks.IsWatchUrl(url));
}
