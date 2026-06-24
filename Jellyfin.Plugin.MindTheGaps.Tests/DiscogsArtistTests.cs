using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Discogs;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The Discogs artist-releases fixture is hand-authored to the documented artists/{id}/releases shape (the
// live API is token-gated). It carries the cases the mapper filters: a non-master "release", a guest
// "Appearance", a master listed twice (same id), and an otherwise-emitted master used for the ownership tests.
public class DiscogsArtistTests
{
    private const long ArtistId = 999;
    private const string ArtistName = "Test Artist";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static OwnershipIndex IndexWith(params string[] keys)
    {
        var dict = new Dictionary<string, BaseItem>();
        foreach (var key in keys)
        {
            dict[key] = null!;
        }

        return new OwnershipIndex(dict);
    }

    private static IReadOnlyList<DiscogsRelease> LoadReleases()
    {
        var response = JsonSerializer.Deserialize<DiscogsLabelReleasesResponse>(
            TestData.Read("discogs_artist_releases.json"),
            Options);
        Assert.NotNull(response);
        Assert.NotNull(response!.Releases);
        return response.Releases!;
    }

    private static IReadOnlyList<GapItem> Build(OwnershipIndex ownership, int cap = 100)
        => DiscogsArtistMapper.Build(ArtistId, ArtistName, LoadReleases(), "owner-guid", ownership, GapPattern.SetCompletion, cap).ToList();

    [Fact]
    public void Build_EmitsOnlyOwnMasterAlbums_DeDuped()
    {
        var gaps = Build(IndexWith());

        // Of the six entries: a non-master "release", a guest "Appearance", and a duplicate master id are all
        // dropped, leaving the three distinct Main masters.
        Assert.Equal(3, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(GapPattern.SetCompletion, g.Pattern));
        Assert.All(gaps, g => Assert.Equal(MediaDomain.Music, g.Domain));
        Assert.All(gaps, g => Assert.Equal(BaseItemKind.MusicAlbum, g.TargetKind));
        Assert.All(gaps, g => Assert.Equal(ArtistName, g.SourceItemName));
        Assert.Contains(gaps, g => g.Name == "First Album");
        Assert.DoesNotContain(gaps, g => g.Name == "First Album (Reissue)");
        Assert.DoesNotContain(gaps, g => g.Name == "Some Compilation");

        var first = gaps.Single(g => g.Name == "First Album");
        Assert.Equal("discogsartist:999:100", first.Id);
        Assert.Equal("100", first.ProviderIds["Discogs"]);
        Assert.Equal(1990, first.Year);
    }

    [Fact]
    public void Build_SkipsAlbumOwnedByDiscogsId()
    {
        var owned = IndexWith(OwnershipIndex.MakeKey(BaseItemKind.MusicAlbum, ProviderIds.Discogs, "104"));

        var gaps = Build(owned);

        Assert.DoesNotContain(gaps, g => g.Name == "Owned Album");
        Assert.Equal(2, gaps.Count);
    }

    [Fact]
    public void Build_SkipsAlbumOwnedByName_AgainstTheOwnedArtistSpelling()
    {
        // The library holds "Second Album" under a MusicBrainz id (no Discogs id); the artist-and-title name
        // match still recognizes it as owned.
        var owned = IndexWith(OwnershipIndex.MakeKey(
            BaseItemKind.MusicAlbum,
            OwnershipIndex.NameKeyProvider,
            OwnershipIndex.NameKey(ArtistName, "Second Album")));

        var gaps = Build(owned);

        Assert.DoesNotContain(gaps, g => g.Name == "Second Album");
        Assert.Equal(2, gaps.Count);
    }

    [Fact]
    public void Build_HonorsCap()
    {
        Assert.Single(Build(IndexWith(), cap: 1));
    }

    [Fact]
    public void Matcher_PicksTheExactNameMatch()
    {
        var results = new List<DiscogsSearchResult>
        {
            new() { Id = 1, Title = "Test Artist Tribute Band" },
            new() { Id = 2, Title = "Test Artist" },
            new() { Id = 3, Title = "Test Artist" }
        };

        // The first exact-name match wins (Discogs ranks by relevance); the longer namesake is not chosen.
        Assert.Equal(2, DiscogsArtistMatcher.Pick(results, "test artist!"));
    }

    [Fact]
    public void Matcher_ReturnsNull_WhenNoExactNameMatch()
    {
        var results = new List<DiscogsSearchResult>
        {
            new() { Id = 1, Title = "Test Artist Tribute Band" },
            new() { Id = 2, Title = "A Different Artist" }
        };

        Assert.Null(DiscogsArtistMatcher.Pick(results, "Test Artist"));
    }

    [Fact]
    public void Matcher_ReturnsNull_OnEmptyInput()
    {
        Assert.Null(DiscogsArtistMatcher.Pick(null, "Test Artist"));
        Assert.Null(DiscogsArtistMatcher.Pick(new List<DiscogsSearchResult>(), "Test Artist"));
    }
}
