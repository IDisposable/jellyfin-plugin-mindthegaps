using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// A title's certification is picked and spelled as Jellyfin's own TMDB provider writes a library item's
// official rating, so core's rating table reads both the same way, and carries the country it came from.
public class TmdbCertificationTests
{
    private static (string? Country, IEnumerable<string?> Certifications) R(string? country, params string?[] certifications)
        => (country, certifications);

    [Fact]
    public void PrefersTheMetadataCountry()
        => Assert.Equal(new TmdbCertification("GB", "GB-15"), TmdbCertification.Pick([R("US", "R"), R("GB", "15")], "gb"));

    [Fact]
    public void FallsBackToTheUs_ThenToTheFirstCountryListed()
    {
        Assert.Equal(new TmdbCertification("US", "R"), TmdbCertification.Pick([R("FR", "16"), R("US", "R")], "GB"));
        Assert.Equal(new TmdbCertification("FR", "FR-16"), TmdbCertification.Pick([R("FR", "16"), R("NL", "12")], "GB"));
    }

    [Fact]
    public void SpellsAUsRatingPlainAndGermanyAsFsk()
    {
        Assert.Equal(new TmdbCertification("US", "PG-13"), TmdbCertification.Pick([R("US", "PG-13")], null));
        Assert.Equal(new TmdbCertification("DE", "FSK-12"), TmdbCertification.Pick([R("de", "12")], "DE"));
    }

    // A movie lists a release per country, often the first without a certification.
    [Fact]
    public void SkipsAReleaseWithNoCertification()
        => Assert.Equal("PG", TmdbCertification.Pick([R("US", "", null, " PG ")], "US")?.Rating);

    [Fact]
    public void ACountryWithNoCertificationIsPassedOver()
        => Assert.Equal("R", TmdbCertification.Pick([R("GB", ""), R("US", "R")], "GB")?.Rating);

    [Fact]
    public void NoCertificationAnywhereIsUnrated()
    {
        Assert.Null(TmdbCertification.Pick([R("US", ""), R("GB")], "US"));
        Assert.Null(TmdbCertification.Pick([], "US"));
    }
}
