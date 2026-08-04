using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Real response captured from OpenLibrary's public reading log, which needs no key. Re-capture with:
//   curl -s "https://openlibrary.org/people/mekBot/books/want-to-read.json?limit=12" -o openlibrary_wanttoread.json
// The shelf has to be public for OpenLibrary to serve it; a private one 404s.
public class OpenLibraryWantToReadTests
{
    private const string User = "mekBot";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Build_EmitsBookRecommendationsForUnownedWorks()
    {
        var gaps = Build(Owns());

        Assert.Equal(12, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(GapPattern.Recommendation, g.Pattern));
        Assert.All(gaps, g => Assert.Equal(MediaDomain.Books, g.Domain));
        Assert.All(gaps, g => Assert.Equal(BaseItemKind.Book, g.TargetKind));
        Assert.All(gaps, g => Assert.Equal("OpenLibraryShelf", g.SourceItemType));
        Assert.All(gaps, g => Assert.Equal("openlibrarywanttoread-mekBot", g.SourceItemId));

        var fifth = gaps.Single(g => g.Name == "The Fifth Season");
        Assert.Equal("openlibrarywanttoread:mekBot:OL17363125W", fifth.Id);

        // The work key is stored bare, matching what a metadata plugin records, so ownership can be diffed.
        Assert.Equal("OL17363125W", fifth.ProviderIds["OpenLibrary"]);
        Assert.Equal(2015, fifth.Year);

        // The shelf names the authors inline, so no second lookup is needed to say who wrote it.
        Assert.Equal("N. K. Jemisin", fifth.Overview);
    }

    [Fact]
    public void Build_SkipsOwnedWorks()
    {
        var gaps = Build(Owns("OL17363125W"));

        Assert.DoesNotContain(gaps, g => g.Name == "The Fifth Season");
        Assert.Equal(11, gaps.Count);
    }

    [Fact]
    public void Build_RespectsCap()
        => Assert.Equal(2, OpenLibraryWantToReadMapper.Build(User, LoadWorks(), Owns(), 2).Count());

    [Fact]
    public void Build_IsUndatedRatherThanUpcomingForABookWithNoYear()
    {
        // Books are the domain where a missing date means "the catalog does not say", not "not out yet".
        var gaps = Build(Owns());
        Assert.All(gaps.Where(g => g.Year is null), g => Assert.False(g.IsUpcoming));
    }

    private static List<GapItem> Build(OwnershipIndex ownership)
        => OpenLibraryWantToReadMapper.Build(User, LoadWorks(), ownership, 100).ToList();

    private static IReadOnlyList<OpenLibraryReadingLogWork> LoadWorks()
    {
        var response = JsonSerializer.Deserialize<OpenLibraryReadingLogResponse>(
            TestData.Read("openlibrary_wanttoread.json"),
            _jsonOptions);
        Assert.NotNull(response?.ReadingLogEntries);
        return response!.ReadingLogEntries!
            .Select(e => e.Work)
            .Where(w => w is not null)
            .Select(w => w!)
            .ToList();
    }

    private static OwnershipIndex Owns(params string[] workIds)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in workIds)
        {
            set.Add(OwnershipIndex.MakeKey(BaseItemKind.Book, "OpenLibrary", id));
        }

        return new OwnershipIndex(set);
    }
}
