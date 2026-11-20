using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.ListenBrainz;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// A real, keyless capture of ListenBrainz's similar artists for Radiohead. Re-capture with:
//   curl -s 'https://labs.api.listenbrainz.org/similar-artists/json?artist_mbids=a74b1b7f-71a5-4011-9441-d0b5e4122711&algorithm=session_based_days_7500_session_300_contribution_5_threshold_10_limit_100_filter_True_skip_30' \
//     -o listenbrainz_similar_artists.json
public class ListenBrainzSimilarArtistsTests
{
    private const string Radiohead = "a74b1b7f-71a5-4011-9441-d0b5e4122711";
    private const string Nirvana = "5b11f4ce-a62d-471e-81fc-a69a8278c7da";
    private const string SeedKey = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void TheCaptureCarriesAnIdAndANameForEverySimilarArtist()
    {
        var similar = Load();

        Assert.Equal(100, similar.Count);
        Assert.All(similar, a => Assert.False(string.IsNullOrWhiteSpace(a.ArtistMbid)));
        Assert.All(similar, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
        Assert.All(similar, a => Assert.Equal(Radiohead, a.ReferenceMbid));
        Assert.Equal("Nirvana", similar[0].Name);
        Assert.True(similar[0].Score >= similar[1].Score);
    }

    [Fact]
    public void Build_MakesADiscoveryGapPerUnownedArtist_ClosestFirst_Capped()
    {
        var gaps = MusicRecommendationMapper.Build(Load(), SeedKey, "Radiohead", Radiohead, OwnsNothing(), 10).ToList();

        Assert.Equal(10, gaps.Count);
        var first = gaps[0];
        Assert.Equal("recommendation:artist:" + Nirvana, first.Id);
        Assert.Equal("Nirvana", first.Name);
        Assert.Equal(GapPattern.Recommendation, first.Pattern);
        Assert.Equal(MediaDomain.Music, first.Domain);
        Assert.Equal(BaseItemKind.MusicArtist, first.TargetKind);
        Assert.Equal(SeedKey, first.SourceItemId);
        Assert.Equal(SourceItemTypes.MusicArtist, first.SourceItemType);
        Assert.Equal(Nirvana, first.ProviderIds[ProviderIds.MusicBrainzArtist]);
        Assert.Contains(first.Links, l => l.Url == "https://musicbrainz.org/artist/" + Nirvana);
        Assert.Contains(first.SourceLinks, l => l.Url == "https://musicbrainz.org/artist/" + Radiohead);
    }

    [Fact]
    public void Build_SkipsAnOwnedArtistAndTheSeedItself()
    {
        var owned = new OwnershipIndex(new HashSet<string>(StringComparer.Ordinal)
        {
            OwnershipIndex.MakeKey(BaseItemKind.MusicArtist, ProviderIds.MusicBrainzArtist, Nirvana)
        });
        var withSeed = new List<ListenBrainzSimilarArtist> { new() { ArtistMbid = Radiohead, Name = "Radiohead" } };
        withSeed.AddRange(Load());

        var gaps = MusicRecommendationMapper.Build(withSeed, SeedKey, "Radiohead", Radiohead, owned, 100).ToList();

        Assert.Equal(99, gaps.Count);
        Assert.DoesNotContain(gaps, g => g.ProviderIds[ProviderIds.MusicBrainzArtist] == Nirvana);
        Assert.DoesNotContain(gaps, g => g.ProviderIds[ProviderIds.MusicBrainzArtist] == Radiohead);
    }

    [Fact]
    public void Build_TwoSeedsSuggestingOneArtistShareItsGapId()
    {
        var one = MusicRecommendationMapper.Build(Load(), "a", "A", Radiohead, OwnsNothing(), 1).Single();
        var two = MusicRecommendationMapper.Build(Load(), "b", "B", "11111111-1111-1111-1111-111111111111", OwnsNothing(), 1).Single();

        Assert.Equal(one.Id, two.Id);
    }

    private static OwnershipIndex OwnsNothing() => new(new HashSet<string>(StringComparer.Ordinal));

    private static List<ListenBrainzSimilarArtist> Load()
        => JsonSerializer.Deserialize<List<ListenBrainzSimilarArtist>>(TestData.Read("listenbrainz_similar_artists.json"))!;
}
