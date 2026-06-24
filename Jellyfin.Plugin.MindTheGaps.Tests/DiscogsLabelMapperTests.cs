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

// The Discogs fixture is hand-authored to the documented labels/{id}/releases shape (the live API is
// token-gated, so it cannot be captured without a personal token). It carries the cases the mapper handles:
// a release listed twice (same id), a release with no artist, and a release with no year.
public class DiscogsLabelMapperTests
{
    private const long LabelId = 23528;

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
            TestData.Read("discogs_label_releases.json"),
            Options);
        Assert.NotNull(response);
        Assert.NotNull(response!.Releases);
        return response.Releases!;
    }

    [Fact]
    public void Build_EmitsSetCompletionGapsForUnownedReleases()
    {
        var gaps = DiscogsLabelMapper.Build(LabelId, "Warp Records", LoadReleases(), IndexWith(), 100).ToList();

        // Four releases, but id 100 is listed twice (de-duped), so three gaps.
        Assert.Equal(3, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(GapPattern.SetCompletion, g.Pattern));
        Assert.All(gaps, g => Assert.Equal(MediaDomain.Music, g.Domain));
        Assert.All(gaps, g => Assert.Equal(BaseItemKind.MusicAlbum, g.TargetKind));
        Assert.All(gaps, g => Assert.Equal("Warp Records", g.SourceItemName));
        Assert.All(gaps, g => Assert.Equal("MusicLabel", g.SourceItemType));

        var first = gaps[0];
        Assert.Equal("Aphex Twin - Selected Ambient Works 85-92", first.Name);
        Assert.Equal("discogslabel:23528:100", first.Id);
        Assert.Equal("100", first.ProviderIds["Discogs"]);
    }

    [Fact]
    public void Build_UsesTitleOnlyWhenArtistMissing_AndNoYearWhenZero()
    {
        var gaps = DiscogsLabelMapper.Build(LabelId, "Warp Records", LoadReleases(), IndexWith(), 100).ToList();

        var untitled = gaps.Single(g => g.Id == "discogslabel:23528:102");
        Assert.Equal("Untitled", untitled.Name);
        Assert.Null(untitled.Year);
    }

    [Fact]
    public void Build_SkipsReleasesOwnedByDiscogsId()
    {
        var owned = IndexWith(OwnershipIndex.MakeKey(BaseItemKind.MusicAlbum, ProviderIds.Discogs, "100"));

        var gaps = DiscogsLabelMapper.Build(LabelId, "Warp Records", LoadReleases(), owned, 100).ToList();

        Assert.DoesNotContain(gaps, g => g.ProviderIds["Discogs"] == "100");
        Assert.Equal(2, gaps.Count);
    }

    [Fact]
    public void Build_SkipsReleasesOwnedByName_WhenNoSharedDiscogsId()
    {
        // The library holds release 100 under a MusicBrainz id (no Discogs id); the artist and title match,
        // so the conservative name fallback still recognizes it as owned.
        var owned = IndexWith(OwnershipIndex.MakeKey(
            BaseItemKind.MusicAlbum,
            OwnershipIndex.NameKeyProvider,
            OwnershipIndex.NameKey("Aphex Twin", "Selected Ambient Works 85-92")));

        var gaps = DiscogsLabelMapper.Build(LabelId, "Warp Records", LoadReleases(), owned, 100).ToList();

        Assert.DoesNotContain(gaps, g => g.ProviderIds["Discogs"] == "100");
        Assert.Equal(2, gaps.Count);
    }

    [Fact]
    public void Build_NameFallback_NormalizesPunctuationAndCase()
    {
        // Case and punctuation differences must not block the name match.
        var owned = IndexWith(OwnershipIndex.MakeKey(
            BaseItemKind.MusicAlbum,
            OwnershipIndex.NameKeyProvider,
            OwnershipIndex.NameKey("APHEX TWIN", "Selected Ambient Works: 85-92!")));

        var gaps = DiscogsLabelMapper.Build(LabelId, "Warp Records", LoadReleases(), owned, 100).ToList();

        Assert.DoesNotContain(gaps, g => g.ProviderIds["Discogs"] == "100");
    }

    [Fact]
    public void Build_NameFallback_RequiresArtist_NotTitleAlone()
    {
        // A different artist with the same title is a different album, so the gap must stand (conservative:
        // the fallback can only fail toward still reporting a gap, never hide one).
        var owned = IndexWith(OwnershipIndex.MakeKey(
            BaseItemKind.MusicAlbum,
            OwnershipIndex.NameKeyProvider,
            OwnershipIndex.NameKey("Someone Else", "Selected Ambient Works 85-92")));

        var gaps = DiscogsLabelMapper.Build(LabelId, "Warp Records", LoadReleases(), owned, 100).ToList();

        Assert.Contains(gaps, g => g.ProviderIds["Discogs"] == "100");
        Assert.Equal(3, gaps.Count);
    }
}
