using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Newtonsoft.Json;
using TMDbLib.Objects.Collections;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Real response captured from api.themoviedb.org/3/collection/2344 (The Matrix Collection).
public class TmdbCollectionCapturedDataTests
{
    private static Collection Load()
        => JsonConvert.DeserializeObject<Collection>(TestData.Read("tmdb_collection.json"))!;

    private static string? Poster(string? path) => path;

    [Fact]
    public void Build_NothingOwned_EveryPartIsAGap()
    {
        var collection = Load();
        var ownership = new OwnershipIndex(new Dictionary<string, BaseItem>());

        var gaps = CollectionGapMapper
            .Build(collection.Id, collection.Parts!, "boxset-id", "The Matrix Collection", ownership, Poster)
            .ToList();

        Assert.Equal(4, gaps.Count);
        Assert.Contains(gaps, g => g.Name == "The Matrix" && g.Year == 1999);
        Assert.Contains(gaps, g => g.Name == "The Matrix Resurrections" && g.Year == 2021);
        Assert.All(gaps, g =>
        {
            Assert.Equal(GapPattern.SetCompletion, g.Pattern);
            Assert.Equal(BaseItemKind.Movie, g.TargetKind);
            Assert.Equal("BoxSet", g.SourceItemType);
        });
    }

    [Fact]
    public void Build_OwnedPartExcluded()
    {
        var collection = Load();
        var owned = new Dictionary<string, BaseItem>
        {
            [OwnershipIndex.MakeKey(BaseItemKind.Movie, "Tmdb", "603")] = new Movie()
        };
        var ownership = new OwnershipIndex(owned);

        var gaps = CollectionGapMapper
            .Build(collection.Id, collection.Parts!, "boxset-id", "The Matrix Collection", ownership, Poster)
            .ToList();

        Assert.Equal(3, gaps.Count);
        Assert.DoesNotContain(gaps, g => g.Name == "The Matrix");
    }
}
