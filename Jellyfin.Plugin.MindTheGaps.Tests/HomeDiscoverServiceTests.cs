using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class HomeDiscoverServiceTests
{
    private static GapItem Rec(string tmdbId, string name, string seedGuid, string seedName, double? score = null, params string[] otherSeeds) => new()
    {
        Id = "recommendation:movie:" + tmdbId,
        Pattern = GapPattern.Recommendation,
        TargetKind = BaseItemKind.Movie,
        Name = name,
        SourceItemId = seedGuid,
        SourceItemName = seedName,
        SourceItemType = "Movie",
        SortScore = score,
        ProviderIds = new Dictionary<string, string> { ["Tmdb"] = tmdbId },
        OtherSources = otherSeeds.Select(n => new GapSourceRef { Id = Guid.NewGuid().ToString("N"), Name = n }).ToList()
    };

    private static readonly IReadOnlyDictionary<string, GapResolution> _none = new Dictionary<string, GapResolution>(StringComparer.Ordinal);

    [Fact]
    public void Rank_OnlyRecommendations_MostAgreedThenMostPopular()
    {
        var items = new List<GapItem>
        {
            Rec("1", "Popular but single-seed", "a", "Seed A", score: 90),
            Rec("2", "Two seeds", "b", "Seed B", score: 10, "Seed C"),
            Rec("3", "Quiet single-seed", "a", "Seed A", score: 5),
            new GapItem { Id = "filmography:movie:9", Pattern = GapPattern.CreatorWorks, TargetKind = BaseItemKind.Movie, Name = "Not a recommendation", ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "9" } },
            new GapItem { Id = "recommendation:album:1", Pattern = GapPattern.Recommendation, TargetKind = BaseItemKind.MusicAlbum, Name = "Wrong kind", ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1" } }
        };

        var ranked = HomeDiscoverService.Rank(items, _none, 10);

        Assert.Equal(["Two seeds", "Popular but single-seed", "Quiet single-seed"], ranked.Select(t => t.Title));
        Assert.Equal("Because you have Seed B, Seed C", ranked[0].Because);
        Assert.Equal("Movie", ranked[0].Kind);
    }

    [Fact]
    public void Rank_HidesDismissedGaps_MutedSeeds_AdhocGaps_AndHonoursTheLimit()
    {
        var adhoc = Rec("4", "Explored", "a", "Seed A", 50);
        adhoc.Adhoc = true;
        var items = new List<GapItem>
        {
            Rec("1", "Kept", "a", "Seed A", 1),
            Rec("2", "Dismissed", "a", "Seed A", 99),
            Rec("3", "From muted seed", "muted", "Muted seed", 99),
            adhoc,
            Rec("5", "Kept too", "a", "Seed A", 2),
            Rec("6", "Over the limit", "a", "Seed A", 0)
        };
        var resolutions = new Dictionary<string, GapResolution>(StringComparer.Ordinal)
        {
            ["recommendation:movie:2"] = new GapResolution { Kind = GapResolution.NotInterested },
            [GapResolution.RecSourcePrefix + "muted"] = new GapResolution { Kind = GapResolution.NotInterested }
        };

        var ranked = HomeDiscoverService.Rank(items, resolutions, 2);

        Assert.Equal(["Kept too", "Kept"], ranked.Select(t => t.Title));
    }

    [Fact]
    public void Because_NamesUpToTwoSeeds_ThenCounts()
    {
        Assert.Null(MissingTitleBuilder.Because(new GapItem { Id = "x", SourceItemName = " " }));
        Assert.Equal("Because you have Fargo", MissingTitleBuilder.Because(Rec("1", "t", "a", "Fargo")));
        Assert.Equal("Because you have Fargo, Blood Simple", MissingTitleBuilder.Because(Rec("1", "t", "a", "Fargo", null, "Blood Simple")));
        // A repeated seed name is counted once.
        Assert.Equal("Because you have Fargo, Blood Simple and 2 more", MissingTitleBuilder.Because(Rec("1", "t", "a", "Fargo", null, "Blood Simple", "Raising Arizona", "Fargo", "Barton Fink")));
    }
}
