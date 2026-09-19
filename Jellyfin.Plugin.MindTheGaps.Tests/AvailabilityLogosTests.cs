using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// A scan carries a gap's offers forward and the availability pass skips a gap it has already checked, so an
// offer stored before logos existed is never looked up again. Fill gives it its provider's logo from the
// catalog instead.
public class AvailabilityLogosTests
{
    private static readonly IReadOnlyDictionary<string, string> _logos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Netflix"] = "https://image.tmdb.org/t/p/w45/n.jpg",
        ["Hulu"] = "https://image.tmdb.org/t/p/w45/h.jpg"
    };

    private static GapItem Gap(params AvailabilityOffer[] offers) => new() { Id = "g", Availability = offers };

    private static AvailabilityOffer Offer(string provider, string? logo = null) => new() { Provider = provider, MonetizationType = "flatrate", LogoUrl = logo };

    [Fact]
    public void Fill_GivesAnOfferWithNoLogoItsProvidersLogo()
    {
        var offer = Offer("Netflix");

        var filled = AvailabilityLogos.Fill([Gap(offer)], _logos);

        Assert.Equal(1, filled);
        Assert.Equal("https://image.tmdb.org/t/p/w45/n.jpg", offer.LogoUrl);
    }

    [Fact]
    public void Fill_LeavesAnExistingLogoAlone()
    {
        var offer = Offer("Netflix", "https://example.test/own.png");

        var filled = AvailabilityLogos.Fill([Gap(offer)], _logos);

        Assert.Equal(0, filled);
        Assert.Equal("https://example.test/own.png", offer.LogoUrl);
    }

    [Fact]
    public void Fill_IgnoresAProviderTheCatalogDoesNotKnow_AndMatchesIgnoringCase()
    {
        var unknown = Offer("Some Small Service");
        var lower = Offer("netflix");

        var filled = AvailabilityLogos.Fill([Gap(unknown, lower)], _logos);

        Assert.Equal(1, filled);
        Assert.Null(unknown.LogoUrl);
        Assert.NotNull(lower.LogoUrl);
    }

    [Fact]
    public void Fill_WithAnEmptyCatalog_ChangesNothing()
    {
        var offer = Offer("Netflix");

        var filled = AvailabilityLogos.Fill([Gap(offer)], new Dictionary<string, string>());

        Assert.Equal(0, filled);
        Assert.Null(offer.LogoUrl);
    }

    [Fact]
    public void Fill_HandlesGapsWithNoOffersAndManyOffers()
    {
        var a = Offer("Netflix");
        var b = Offer("Hulu");

        var filled = AvailabilityLogos.Fill([Gap(), Gap(a, b)], _logos);

        Assert.Equal(2, filled);
        Assert.NotNull(a.LogoUrl);
        Assert.NotNull(b.LogoUrl);
    }
}
