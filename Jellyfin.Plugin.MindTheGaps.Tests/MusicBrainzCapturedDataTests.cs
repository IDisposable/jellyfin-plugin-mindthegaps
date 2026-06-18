using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.MusicBrainz;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Captured from the live MusicBrainz API (keyless, public; send a descriptive User-Agent). To recapture:
//   curl -s -H 'User-Agent: Jellyfin.Plugin.MindTheGaps/1.0 (https://github.com/IDisposable)' \
//     'https://musicbrainz.org/ws/2/release-group?artist=b10bbbfc-cf9e-42e0-be17-e2c3e1d2600d&type=album&fmt=json&limit=100' \
//     > musicbrainz_releasegroups.json
// (artist b10bbbfc-cf9e-42e0-be17-e2c3e1d2600d is The Beatles; the response carries no key, safe to commit.)
public class MusicBrainzCapturedDataTests
{
    private const string BeatlesMbid = "b10bbbfc-cf9e-42e0-be17-e2c3e1d2600d";
    private const string AbbeyRoadMbid = "9162580e-5df4-32de-80cc-f45a8d8a9b1d";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    // Owns/OwnsAny only test key membership, so null item values are sufficient here.
    private static OwnershipIndex IndexWith(params string[] keys)
    {
        var dict = new Dictionary<string, BaseItem>();
        foreach (var key in keys)
        {
            dict[key] = null!;
        }

        return new OwnershipIndex(dict);
    }

    private static IReadOnlyList<MusicBrainzReleaseGroup> Load()
    {
        var response = JsonSerializer.Deserialize<MusicBrainzReleaseGroupResponse>(
            TestData.Read("musicbrainz_releasegroups.json"),
            Options);
        Assert.NotNull(response);
        Assert.NotNull(response!.ReleaseGroups);
        return response.ReleaseGroups!;
    }

    [Fact]
    public void Response_ParsesReleaseGroups()
    {
        var groups = Load();
        Assert.Equal(100, groups.Count);
        Assert.Equal("A Collection of Beatles Oldies", groups[0].Title);
        Assert.Equal("Album", groups[0].PrimaryType);
    }

    [Fact]
    public void IsStudioAlbum_FiltersCompilationsAndLive()
    {
        var groups = Load();
        var studio = groups.Where(MusicBrainzMapper.IsStudioAlbum).Select(g => g.Title).ToList();

        Assert.Equal(18, studio.Count);
        Assert.Contains("Abbey Road", studio);
        Assert.DoesNotContain("A Collection of Beatles Oldies", studio);
        Assert.DoesNotContain("Anthology 3", studio);
    }

    [Fact]
    public void Build_EmitsGapsForUnownedStudioAlbums()
    {
        var groups = Load();
        var gaps = MusicBrainzMapper.Build(BeatlesMbid, groups, "owner-guid", "The Beatles", IndexWith()).ToList();

        Assert.Equal(18, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(GapPattern.SetCompletion, g.Pattern));
        Assert.All(gaps, g => Assert.Equal(MediaDomain.Music, g.Domain));
        Assert.All(gaps, g => Assert.Equal(BaseItemKind.MusicAlbum, g.TargetKind));

        var abbey = gaps.Single(g => g.Name == "Abbey Road");
        Assert.Equal("discography:" + BeatlesMbid + ":" + AbbeyRoadMbid, abbey.Id);
        Assert.Equal(1969, abbey.Year);
        Assert.Equal("MusicArtist", abbey.SourceItemType);
    }

    [Fact]
    public void Build_SkipsOwnedAlbums()
    {
        var groups = Load();
        var owned = IndexWith(OwnershipIndex.MakeKey(
            BaseItemKind.MusicAlbum,
            MusicBrainzMapper.ReleaseGroupProvider,
            AbbeyRoadMbid));

        var gaps = MusicBrainzMapper.Build(BeatlesMbid, groups, "owner-guid", "The Beatles", owned).ToList();

        Assert.DoesNotContain(gaps, g => g.Name == "Abbey Road");
        Assert.Equal(17, gaps.Count);
    }
}
