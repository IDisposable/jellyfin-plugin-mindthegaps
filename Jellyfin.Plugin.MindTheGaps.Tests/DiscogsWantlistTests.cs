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

// Real response captured from a public Discogs wantlist. Discogs serves a public wantlist to any
// authenticated caller and answers "authenticate as the owner" for a private one, so the capture below needs
// a token even though the data is public. Re-capture with:
//   curl -s -H "Authorization: Discogs token=<YOUR_TOKEN>" \
//     "https://api.discogs.com/users/soundsgood/wants?per_page=8" -o discogs_wantlist.json
// The response carries no token, so it is safe to commit.
public class DiscogsWantlistTests
{
    private const string User = "soundsgood";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Build_EmitsMusicRecommendationsKeyedOnTheReleaseId()
    {
        var gaps = Build(Owns());

        Assert.Single(gaps);
        var want = gaps[0];
        Assert.Equal(GapPattern.Recommendation, want.Pattern);
        Assert.Equal(MediaDomain.Music, want.Domain);
        Assert.Equal(BaseItemKind.MusicAlbum, want.TargetKind);
        Assert.Equal("DiscogsWantlist", want.SourceItemType);
        Assert.Equal("discogswantlist-soundsgood", want.SourceItemId);
        Assert.Equal("discogswantlist:soundsgood:1542479", want.Id);
        Assert.Equal("Bye Bye Baby Goodbye", want.Name);
        Assert.Equal("1542479", want.ProviderIds["Discogs"]);
        Assert.Equal(1992, want.Year);
    }

    [Fact]
    public void Build_StripsTheDiscogsDuplicateNameSuffixFromTheCredit()
    {
        // Discogs disambiguates artists that share a name with a numeric suffix ("Rainbow (11)"). That is a
        // database detail, not part of the name, and it reads as noise on a report row.
        Assert.Equal("Rainbow", Build(Owns())[0].Overview);
    }

    [Fact]
    public void Build_SkipsOwnedReleases()
        => Assert.Empty(Build(Owns("1542479")));

    [Fact]
    public void Build_RespectsCap()
        => Assert.Empty(DiscogsWantlistMapper.Build(User, LoadWants(), Owns(), 0));

    private static List<GapItem> Build(OwnershipIndex ownership)
        => DiscogsWantlistMapper.Build(User, LoadWants(), ownership, 100).ToList();

    private static IReadOnlyList<DiscogsWant> LoadWants()
    {
        var response = JsonSerializer.Deserialize<DiscogsWantlistResponse>(
            TestData.Read("discogs_wantlist.json"),
            _jsonOptions);
        Assert.NotNull(response?.Wants);
        return response!.Wants!;
    }

    private static OwnershipIndex Owns(params string[] releaseIds)
    {
        var dict = new Dictionary<string, BaseItem>();
        foreach (var id in releaseIds)
        {
            dict[OwnershipIndex.MakeKey(BaseItemKind.MusicAlbum, "Discogs", id)] = null!;
        }

        return new OwnershipIndex(dict);
    }
}
