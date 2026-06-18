using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.OpenLibrary;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Captured from the live OpenLibrary API (keyless, public). To recapture:
//   curl -s 'https://openlibrary.org/search/authors.json?q=Frank%20Herbert' > openlibrary_authorsearch.json
//   curl -s 'https://openlibrary.org/authors/OL79034A/works.json?limit=100' > openlibrary_works.json
//
// The real data exposes limitations of the (experimental) Books source these tests now document:
//  - the author search's first result is a different "Frank Herbert" (Hayward); the Dune author OL79034A
//    is third, so a naive "first doc" best-match is wrong (see roadmap, Books-source hardening);
//  - the works-list response carries no publish dates, so book gaps get no year;
//  - several works share a title ("Dune" appears more than once as distinct work keys).
public class OpenLibraryCapturedDataTests
{
    private const string AuthorKey = "OL79034A";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static OwnershipIndex IndexWith(params string[] keys)
    {
        var dict = new Dictionary<string, BaseItem>();
        foreach (var key in keys)
        {
            dict[key] = null!;
        }

        return new OwnershipIndex(dict);
    }

    private static IReadOnlyList<OpenLibraryWork> LoadWorks()
    {
        var response = JsonSerializer.Deserialize<OpenLibraryWorksResponse>(
            TestData.Read("openlibrary_works.json"),
            Options);
        Assert.NotNull(response);
        Assert.NotNull(response!.Entries);
        return response.Entries!;
    }

    [Fact]
    public void AuthorSearch_ParsesDocs_AndFirstResultIsAmbiguous()
    {
        var response = JsonSerializer.Deserialize<OpenLibraryAuthorSearchResponse>(
            TestData.Read("openlibrary_authorsearch.json"),
            Options);

        Assert.NotNull(response);

        // The intended author is present and parses, but is not the first result.
        var herbert = response!.Docs!.Single(d => d.Key == AuthorKey);
        Assert.Equal("Frank Herbert", herbert.Name);

        // The first result is a different "Frank Herbert", so picking docs[0] would resolve the wrong
        // author. This is a known limitation of the experimental Books source (see roadmap).
        Assert.NotEqual(AuthorKey, response.Docs!.First().Key);
    }

    [Fact]
    public void Works_ParseEntries()
    {
        var works = LoadWorks();
        Assert.Equal(100, works.Count);
        Assert.Equal("Duna", works[0].Title);
        Assert.Equal("/works/OL45588324W", works[0].Key);
    }

    [Fact]
    public void NormalizeWorkKey_StripsWorksPrefix()
    {
        Assert.Equal("OL893415W", OpenLibraryMapper.NormalizeWorkKey("/works/OL893415W"));
        Assert.Equal("OL27482W", OpenLibraryMapper.NormalizeWorkKey("OL27482W"));
    }

    [Fact]
    public void Build_EmitsCreatorWorksGapsForUnownedTitles()
    {
        var works = LoadWorks();
        var gaps = OpenLibraryMapper.Build(AuthorKey, "Frank Herbert", works, "owner-guid", IndexWith(), 20).ToList();

        Assert.Equal(20, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(GapPattern.CreatorWorks, g.Pattern));
        Assert.All(gaps, g => Assert.Equal(MediaDomain.Books, g.Domain));
        Assert.All(gaps, g => Assert.Equal(BaseItemKind.Book, g.TargetKind));

        // First work in the response, keyed by its normalized work id; the works endpoint carries no
        // publish date, so there is no year.
        var first = gaps[0];
        Assert.Equal("Duna", first.Name);
        Assert.Equal("bibliography:" + AuthorKey + ":OL45588324W", first.Id);
        Assert.Null(first.Year);
    }

    [Fact]
    public void Build_SkipsOwnedWorksAndHonorsCap()
    {
        var works = LoadWorks();
        var owned = IndexWith(OwnershipIndex.MakeKey(BaseItemKind.Book, OpenLibraryMapper.OpenLibraryProvider, "OL45588324W"));

        var gaps = OpenLibraryMapper.Build(AuthorKey, "Frank Herbert", works, "owner-guid", owned, 2).ToList();

        Assert.DoesNotContain(gaps, g => g.Id == "bibliography:" + AuthorKey + ":OL45588324W");
        Assert.Equal(2, gaps.Count);
    }
}
