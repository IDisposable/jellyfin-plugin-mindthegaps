using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.MdbList;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.MdbList;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class MdbListMapperTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyList<MdbListItem> LoadItems()
    {
        var response = JsonSerializer.Deserialize<MdbListItemsResponse>(
            TestData.Read("mdblist_items.json"),
            _jsonOptions);
        Assert.NotNull(response);

        var items = new List<MdbListItem>();
        if (response!.Movies is not null)
        {
            items.AddRange(response.Movies);
        }

        if (response.Shows is not null)
        {
            items.AddRange(response.Shows);
        }

        return items;
    }

    private static OwnershipIndex OwnsTmdb(BaseItemKind kind, params int[] tmdbIds)
    {
        var dict = new Dictionary<string, BaseItem>();
        foreach (var id in tmdbIds)
        {
            dict[OwnershipIndex.MakeKey(kind, "Tmdb", id.ToString(CultureInfo.InvariantCulture))] = null!;
        }

        return new OwnershipIndex(dict);
    }

    [Fact]
    public void Build_EmitsRecommendationGaps_RoutesByMediaType_DropsItemsWithNoIds()
    {
        var gaps = MdbListMapper.Build(42, "Top Sci-Fi", LoadItems(), OwnsTmdb(BaseItemKind.Movie), 100).ToList();

        // The Matrix and Another Film (movies) plus Breaking Bad (show); "No Ids Film" is dropped.
        Assert.Equal(3, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(GapPattern.Recommendation, g.Pattern));
        Assert.All(gaps, g => Assert.Equal("MdbList", g.SourceItemType));
        Assert.All(gaps, g => Assert.Equal("Top Sci-Fi", g.SourceItemName));
        Assert.DoesNotContain(gaps, g => g.Name == "No Ids Film");

        var matrix = gaps.Single(g => g.Name == "The Matrix");
        Assert.Equal("mdblist:42:603", matrix.Id);
        Assert.Equal(MediaDomain.Movies, matrix.Domain);
        Assert.Equal(BaseItemKind.Movie, matrix.TargetKind);
        Assert.Equal("603", matrix.ProviderIds["Tmdb"]);

        var breakingBad = gaps.Single(g => g.Name == "Breaking Bad");
        Assert.Equal(MediaDomain.Shows, breakingBad.Domain);
        Assert.Equal(BaseItemKind.Series, breakingBad.TargetKind);
        Assert.Equal("81189", breakingBad.ProviderIds["Tvdb"]);
    }

    [Fact]
    public void Build_SkipsOwnedByTmdbId()
    {
        var gaps = MdbListMapper.Build(42, "Top Sci-Fi", LoadItems(), OwnsTmdb(BaseItemKind.Movie, 603), 100).ToList();

        Assert.DoesNotContain(gaps, g => g.Name == "The Matrix");
        Assert.Contains(gaps, g => g.Name == "Another Film");
        Assert.Contains(gaps, g => g.Name == "Breaking Bad");
    }
}
