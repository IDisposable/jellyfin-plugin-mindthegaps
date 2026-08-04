using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.MdbList;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.MdbList;
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
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in tmdbIds)
        {
            set.Add(OwnershipIndex.MakeKey(kind, "Tmdb", id.ToString(CultureInfo.InvariantCulture)));
        }

        return new OwnershipIndex(set);
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

    [Fact]
    public void BuildWatchlist_KeysOnTheTitleAloneAndOwnsItsOwnSource()
    {
        // The account's watchlist arrives in the same envelope a list does, so the same walk serves it; only
        // the id and the owner differ, because there is one watchlist rather than one of many lists.
        var gaps = MdbListMapper.BuildWatchlist(LoadItems(), OwnsTmdb(BaseItemKind.Movie), 100).ToList();

        Assert.Equal(3, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(GapPattern.Recommendation, g.Pattern));
        Assert.All(gaps, g => Assert.Equal("MdbListWatchlist", g.SourceItemType));
        Assert.All(gaps, g => Assert.Equal("mdblistwatchlist", g.SourceItemId));
        Assert.All(gaps, g => Assert.Equal("MDBList watchlist", g.SourceItemName));

        var matrix = gaps.Single(g => g.Name == "The Matrix");
        Assert.Equal("mdblistwatchlist:603", matrix.Id);
        Assert.Equal("603", matrix.ProviderIds["Tmdb"]);
    }

    [Fact]
    public void BuildWatchlist_SkipsOwnedByTmdbId()
    {
        var gaps = MdbListMapper.BuildWatchlist(LoadItems(), OwnsTmdb(BaseItemKind.Movie, 603), 100).ToList();

        Assert.DoesNotContain(gaps, g => g.Name == "The Matrix");
        Assert.Equal(2, gaps.Count);
    }

    [Theory]
    [InlineData("top sci fi", "top sci fi")]      // A clean query passes through unchanged.
    [InlineData("topl;i", "topli")]               // The semicolon is dropped; no space is inserted in its place.
    [InlineData("  Top   Movies  ", "Top Movies")] // Leading, trailing, and runs of whitespace collapse to one space.
    [InlineData("year 2024", "year 2024")]        // Digits are kept.
    [InlineData(";,.!?", "")]                      // An all-punctuation query becomes empty.
    [InlineData("", "")]                           // An empty query stays empty.
    [InlineData(null, "")]                         // A null query is treated as empty.
    public void SanitizeQuery_KeepsLettersDigitsSpaces_DropsPunctuation(string? input, string expected)
    {
        Assert.Equal(expected, MdbListClient.SanitizeQuery(input));
    }
}
