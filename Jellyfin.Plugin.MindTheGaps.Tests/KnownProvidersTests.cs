using Jellyfin.Plugin.MindTheGaps.Services;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class KnownProvidersTests
{
    [Theory]
    [InlineData("TheMovieDb")] // fetcher name
    [InlineData("Tmdb")]       // id key
    [InlineData("TMDB")]       // service label
    [InlineData("tmdb")]       // case-insensitive
    public void Matches_RecognizesEverySpellingOfAProvider(string name)
        => Assert.True(KnownProviders.Tmdb.Matches(name));

    [Fact]
    public void Matches_RejectsAnotherProvidersNameAndNull()
    {
        Assert.False(KnownProviders.Tmdb.Matches("TheTVDB"));
        Assert.False(KnownProviders.Tmdb.Matches(null));
    }

    [Fact]
    public void ForName_ResolvesAnyNameToItsProvider()
    {
        Assert.Same(KnownProviders.Tvdb, KnownProviders.ForName("TheTVDB"));
        Assert.Same(KnownProviders.Tvdb, KnownProviders.ForName("Tvdb"));
        Assert.Same(KnownProviders.Tmdb, KnownProviders.ForName("TMDB"));
        Assert.Null(KnownProviders.ForName("Nope"));
        Assert.Null(KnownProviders.ForName(null));
    }
}
