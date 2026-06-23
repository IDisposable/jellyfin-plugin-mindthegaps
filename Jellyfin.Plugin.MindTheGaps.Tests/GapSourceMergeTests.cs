using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// A missing title can be surfaced by several sources but collapses to one gap. These cover how the surviving
// gap's sources are folded: a curated list outranks a per-title recommendation for the primary source.
public class GapSourceMergeTests
{
    private static GapItem Rec(string sourceName, string sourceId) => new()
    {
        Id = "movie:1",
        Name = "Some Movie",
        Pattern = GapPattern.Recommendation,
        SourceItemId = sourceId,
        SourceItemName = sourceName,
        SourceItemType = "Movie",
        SourceItemYear = 2010
    };

    private static GapItem List(string sourceName, string sourceId) => new()
    {
        Id = "movie:1",
        Name = "Some Movie",
        Pattern = GapPattern.Recommendation,
        SourceItemId = sourceId,
        SourceItemName = sourceName,
        SourceItemType = "List"
    };

    [Fact]
    public void Merge_PromotesCuratedListOverRecommendation()
    {
        var existing = Rec("Inception", "rec-10");
        var duplicate = List("Best Picture Winners", "tmdblist-28");

        GapSourceMerge.Merge(existing, duplicate);

        // The list claims the primary (grouping) source; the recommendation rides along as a secondary.
        Assert.Equal("Best Picture Winners", existing.SourceItemName);
        Assert.Equal("List", existing.SourceItemType);
        Assert.NotNull(existing.OtherSources);
        Assert.Contains(existing.OtherSources!, s => s.Name == "Inception");
    }

    [Fact]
    public void Merge_KeepsListPrimary_WhenRecommendationIsTheDuplicate()
    {
        var existing = List("Best Picture Winners", "tmdblist-28");
        var duplicate = Rec("Inception", "rec-10");

        GapSourceMerge.Merge(existing, duplicate);

        Assert.Equal("Best Picture Winners", existing.SourceItemName);
        Assert.Contains(existing.OtherSources!, s => s.Name == "Inception");
    }

    [Fact]
    public void Merge_AccumulatesRecommendationSources()
    {
        var existing = Rec("Inception", "rec-10");
        var duplicate = Rec("The Matrix", "rec-20");

        GapSourceMerge.Merge(existing, duplicate);

        Assert.Equal("Inception", existing.SourceItemName);
        Assert.Contains(existing.OtherSources!, s => s.Name == "The Matrix");
    }

    [Fact]
    public void Merge_IgnoresNonRecommendationGaps()
    {
        var existing = new GapItem
        {
            Id = "movie:1",
            Name = "Some Movie",
            Pattern = GapPattern.SetCompletion,
            SourceItemId = "coll-1",
            SourceItemName = "A Collection",
            SourceItemType = "BoxSet"
        };
        var duplicate = List("Best Picture Winners", "tmdblist-28");

        GapSourceMerge.Merge(existing, duplicate);

        Assert.Equal("A Collection", existing.SourceItemName);
        Assert.Null(existing.OtherSources);
    }
}
