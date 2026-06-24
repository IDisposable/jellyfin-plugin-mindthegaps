using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ProviderIdsTests
{
    // The id keys are a persistence contract (ADR-0008): they end up in saved gap ids and ProviderIds maps,
    // so a value drifting (the enum's ToString changing, a literal being mistyped) would orphan saved data.
    [Theory]
    [InlineData("Tmdb")]
    [InlineData("Tvdb")]
    [InlineData("Imdb")]
    [InlineData("MusicBrainzReleaseGroup")]
    [InlineData("MusicBrainzArtist")]
    [InlineData("TVmaze")]
    [InlineData("Discogs")]
    [InlineData("OpenLibrary")]
    public void Key_HasItsExpectedValue(string expected)
    {
        var actual = expected switch
        {
            "Tmdb" => ProviderIds.Tmdb,
            "Tvdb" => ProviderIds.Tvdb,
            "Imdb" => ProviderIds.Imdb,
            "MusicBrainzReleaseGroup" => ProviderIds.MusicBrainzReleaseGroup,
            "MusicBrainzArtist" => ProviderIds.MusicBrainzArtist,
            "TVmaze" => ProviderIds.TVmaze,
            "Discogs" => ProviderIds.Discogs,
            "OpenLibrary" => ProviderIds.OpenLibrary,
            _ => null
        };

        Assert.Equal(expected, actual);
    }
}
