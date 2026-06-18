using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using TMDbLib.Objects.Search;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class CuratedSetGapMapperTests
{
    private static OwnershipIndex OwnsMovie(params int[] tmdbIds)
    {
        var dict = new Dictionary<string, BaseItem>();
        foreach (var id in tmdbIds)
        {
            dict[OwnershipIndex.MakeKey(BaseItemKind.Movie, "Tmdb", id.ToString(System.Globalization.CultureInfo.InvariantCulture))] = null!;
        }

        return new OwnershipIndex(dict);
    }

    private static SearchMovie Movie(int id, string title) => new() { Id = id, Title = title };

    [Fact]
    public void BuildMovies_SkipsOwned_AndEmitsUnowned_WithStableIds()
    {
        var results = new[] { Movie(1, "Owned"), Movie(2, "Missing A"), Movie(3, "Missing B") };

        var gaps = CuratedSetGapMapper.BuildMovies(
            results,
            "company:41077",
            "A24",
            "Studio",
            OwnsMovie(1),
            _ => null,
            perSet: 100).ToList();

        Assert.Equal(2, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(GapPattern.SetCompletion, g.Pattern));
        Assert.All(gaps, g => Assert.Equal(MediaDomain.Movies, g.Domain));
        Assert.All(gaps, g => Assert.Equal("A24", g.SourceItemName));
        Assert.All(gaps, g => Assert.Equal("Studio", g.SourceItemType));
        Assert.Contains(gaps, g => g.Id == "curated:company:41077:2");
        Assert.Contains(gaps, g => g.Id == "curated:company:41077:3");
        Assert.DoesNotContain(gaps, g => g.Name == "Owned");
    }

    [Fact]
    public void BuildMovies_RespectsPerSetCap()
    {
        var results = Enumerable.Range(10, 20).Select(i => Movie(i, "Film " + i)).ToArray();

        var gaps = CuratedSetGapMapper.BuildMovies(
            results,
            "keyword:9715",
            "Keyword 9715",
            "Keyword",
            OwnsMovie(),
            _ => null,
            perSet: 5).ToList();

        Assert.Equal(5, gaps.Count);
    }
}
