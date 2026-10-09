using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// A gap's offers are stored lean: its watch page once, each offer as service, monetization and quality, and each
// service's logo once in the meta file. Reloading gives every offer all of it back, and a file of the plain
// offer arrays an earlier version wrote still reads.
public sealed class StoredAvailabilityTests : IDisposable
{
    private const string Watch = "https://www.themoviedb.org/movie/603/watch?locale=US";
    private const string NetflixLogo = "https://image.tmdb.org/t/p/w92/netflix.jpg";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mtg-offers-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void Save_WritesTheWatchPageOnceAndNoLogos_AndLoadRestoresBoth()
    {
        Save(Gap("a", Offer("Netflix", "flatrate", Watch, NetflixLogo), Offer("Netflix", "ads", Watch, NetflixLogo), Offer("Apple TV", "buy", Watch, null)), Gap("b", Offer("netflix", "flatrate", Watch, null)));

        var json = File.ReadAllText(Path.Combine(_dir, "gaps-movies.json"));
        Assert.Equal(2, Count(json, "~tmdb/movie/603/watch"));
        Assert.DoesNotContain("logoUrl", json, StringComparison.Ordinal);
        Assert.Contains(NetflixLogo, File.ReadAllText(Path.Combine(_dir, "gaps-meta.json")), StringComparison.Ordinal);

        var loaded = Load();
        var a = loaded.Single(g => g.Id == "a");
        Assert.Equal(3, a.Availability.Count);
        Assert.All(a.Availability, o => Assert.Equal(Watch, o.Url));
        Assert.Equal(NetflixLogo, a.Availability[0].LogoUrl);
        Assert.Equal(NetflixLogo, a.Availability[1].LogoUrl);
        Assert.Null(a.Availability[2].LogoUrl);
        Assert.Equal("ads", a.Availability[1].MonetizationType);

        // The logo a service has anywhere reaches the offers of it stored without one, whatever the name's case.
        Assert.Equal(NetflixLogo, loaded.Single(g => g.Id == "b").Availability[0].LogoUrl);
    }

    [Fact]
    public void Load_SharesTheRepeatedStringsAcrossOffers()
    {
        Save(Gap("a", Offer("Netflix", "flatrate", Watch, NetflixLogo)), Gap("b", Offer("Netflix", "flatrate", Watch, NetflixLogo)));

        var loaded = Load();
        var first = loaded[0].Availability[0];
        var second = loaded[1].Availability[0];
        Assert.Same(first.Provider, second.Provider);
        Assert.Same(first.MonetizationType, second.MonetizationType);
        Assert.Same(first.LogoUrl, second.LogoUrl);
    }

    [Fact]
    public void Save_KeepsAnOffersOwnPageWhenTheyDiffer()
    {
        Save(Gap("a", Offer("Netflix", "flatrate", "https://www.netflix.com/title/1", null), Offer("Hulu", "flatrate", "https://www.hulu.com/movie/1", null)));

        var offers = Load().Single().Availability;

        Assert.Equal("https://www.netflix.com/title/1", offers[0].Url);
        Assert.Equal("https://www.hulu.com/movie/1", offers[1].Url);
    }

    [Fact]
    public void Save_GapWithoutOffers_ReadsBackEmpty()
    {
        Save(Gap("a"));

        Assert.Empty(Load().Single().Availability);
    }

    [Fact]
    public void Load_ReadsTheOfferArraysAnEarlierVersionWrote()
    {
        var old = Gap("a", Offer("Netflix", "flatrate", Watch, NetflixLogo));
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "gaps-meta.json"), JsonSerializer.Serialize(new GapReport { GeneratedUtc = DateTime.UtcNow, TotalGaps = 1 }, web));
        File.WriteAllText(Path.Combine(_dir, "gaps-movies.json"), JsonSerializer.Serialize(new[] { old }, web));

        var offer = Assert.Single(Load().Single().Availability);

        Assert.Equal("Netflix", offer.Provider);
        Assert.Equal(Watch, offer.Url);
        Assert.Equal(NetflixLogo, offer.LogoUrl);
    }

    private static int Count(string text, string part)
        => (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;

    private static AvailabilityOffer Offer(string provider, string monetization, string url, string? logo)
        => new() { Provider = provider, MonetizationType = monetization, Url = url, LogoUrl = logo };

    private static GapItem Gap(string id, params AvailabilityOffer[] offers) => new()
    {
        Id = id,
        Name = id,
        Domain = MediaDomain.Movies,
        TargetKind = BaseItemKind.Movie,
        ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" },
        AvailabilityChecked = true,
        Availability = offers
    };

    private void Save(params GapItem[] gaps)
        => new GapStore(NullLogger<GapStore>.Instance, _dir).Save(new GapReport { GeneratedUtc = DateTime.UtcNow, TotalGaps = gaps.Length, Items = gaps });

    private IReadOnlyList<GapItem> Load() => new GapStore(NullLogger<GapStore>.Instance, _dir).Load().Items;
}
