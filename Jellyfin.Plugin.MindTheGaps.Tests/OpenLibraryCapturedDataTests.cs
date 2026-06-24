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
//   curl -s 'https://openlibrary.org/works/OL893415W.json' > openlibrary_workdetail.json
//   curl -s 'https://openlibrary.org/search.json?author_key=OL79034A&fields=key,title,first_publish_year&limit=100' > openlibrary_authorworks_search.json
//
// The real data carries the rough edges the Books-source hardening handles, which these tests exercise:
//  - the author search's first result is a different "Frank Herbert" (Hayward); the Dune author OL79034A is
//    further down, so OpenLibraryAuthorMatcher picks by shortest exact name and work count, not docs[0];
//  - several works share a title ("Dune" appears more than once as distinct work keys), which the mapper
//    de-duplicates to one gap per title;
//  - the author-works endpoint carries no publish dates, so the source reads an owned work's author directly
//    (works/{key}.json names the author, dodging the name search) and lists works via search.json (which
//    carries first_publish_year). The work-detail and author-works-search fixtures cover those two calls.
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
        // author. This is a known limitation of the Books source (see roadmap).
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
    public void WorkDetail_ResolvesAuthorKey()
    {
        var detail = JsonSerializer.Deserialize<OpenLibraryWorkDetail>(
            TestData.Read("openlibrary_workdetail.json"),
            Options);

        Assert.NotNull(detail);

        // Reading a work's author directly (works/{key}.json) sidesteps the name search's namesake problem:
        // Dune's record names the real Frank Herbert (OL79034A), the same author the other fixtures use.
        var authorKey = detail!.Authors!
            .Select(a => a.Author?.Key)
            .FirstOrDefault(k => !string.IsNullOrEmpty(k));
        Assert.Equal("/authors/" + AuthorKey, authorKey);
        Assert.Equal(AuthorKey, authorKey!.Split('/').Last());
    }

    [Fact]
    public void AuthorWorksSearch_ParsesDocsWithYears()
    {
        var response = JsonSerializer.Deserialize<OpenLibrarySearchResponse>(
            TestData.Read("openlibrary_authorworks_search.json"),
            Options);

        Assert.NotNull(response);
        Assert.NotNull(response!.Docs);
        Assert.Equal(100, response.Docs!.Count);

        // Unlike the author-works endpoint, search results carry the first publish year, which is the whole
        // reason the source switched to it: Dune resolves with its 1965 date.
        var dune = response.Docs!.First(d => d.Key == "/works/OL893415W");
        Assert.Equal("Dune", dune.Title);
        Assert.Equal(1965, dune.FirstPublishYear);
        Assert.Contains(response.Docs!, d => d.FirstPublishYear.HasValue);
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
        var owned = IndexWith(OwnershipIndex.MakeKey(BaseItemKind.Book, ProviderIds.OpenLibrary, "OL45588324W"));

        var gaps = OpenLibraryMapper.Build(AuthorKey, "Frank Herbert", works, "owner-guid", owned, 2).ToList();

        Assert.DoesNotContain(gaps, g => g.Id == "bibliography:" + AuthorKey + ":OL45588324W");
        Assert.Equal(2, gaps.Count);
    }

    [Fact]
    public void AuthorMatcher_PicksTheProlificExactName_NotTheFirstResult()
    {
        var response = JsonSerializer.Deserialize<OpenLibraryAuthorSearchResponse>(
            TestData.Read("openlibrary_authorsearch.json"),
            Options);

        var key = OpenLibraryAuthorMatcher.Pick(response!.Docs, "Frank Herbert");

        // The Dune author (exact name, by far the most works) is chosen over the first result (Frank Herbert
        // Hayward, a longer name) and the thin exact-name namesakes, so the namesake problem is handled.
        Assert.Equal(AuthorKey, key);
        Assert.NotEqual(response.Docs!.First().Key, key);
    }

    [Fact]
    public void Build_DeDuplicatesWorksThatShareATitle()
    {
        var works = LoadWorks();

        var gaps = OpenLibraryMapper.Build(AuthorKey, "Frank Herbert", works, "owner-guid", IndexWith(), 100).ToList();

        // OpenLibrary lists the same title as several distinct works ("Dune" appears more than once); the
        // mapper now emits one gap per title, so the de-dup collapses the 100 works to fewer and no two gaps
        // share a normalized title.
        Assert.True(gaps.Count < works.Count);
        var titleKeys = gaps.Select(g => NormalizeForTest(g.Name)).ToList();
        Assert.Equal(titleKeys.Count, titleKeys.Distinct().Count());
    }

    private static string NormalizeForTest(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
