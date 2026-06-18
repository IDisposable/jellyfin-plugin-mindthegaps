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

// SYNTHETIC FIXTURES. Network capture was unavailable in the build environment, so
// openlibrary_authorsearch.json and openlibrary_works.json are hand-built to match the real shape of
// the OpenLibrary responses. The author/work keys are real OpenLibrary identifiers (Frank Herbert).
//
// To (re)capture the real responses (keyless, public):
//   curl -s 'https://openlibrary.org/search/authors.json?q=Frank%20Herbert' > openlibrary_authorsearch.json
//   curl -s 'https://openlibrary.org/authors/OL79034A/works.json?limit=100' > openlibrary_works.json
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
    public void AuthorSearch_ParsesBestMatchKey()
    {
        var response = JsonSerializer.Deserialize<OpenLibraryAuthorSearchResponse>(
            TestData.Read("openlibrary_authorsearch.json"),
            Options);

        Assert.NotNull(response);
        var best = response!.Docs!.First();
        Assert.Equal(AuthorKey, best.Key);
        Assert.Equal("Frank Herbert", best.Name);
    }

    [Fact]
    public void Works_ParseEntries()
    {
        var works = LoadWorks();
        Assert.Equal(4, works.Count);
        Assert.Equal("Dune", works[0].Title);
        Assert.Equal("/works/OL893415W", works[0].Key);
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

        Assert.Equal(4, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(GapPattern.CreatorWorks, g.Pattern));
        Assert.All(gaps, g => Assert.Equal(MediaDomain.Books, g.Domain));
        Assert.All(gaps, g => Assert.Equal(BaseItemKind.Book, g.TargetKind));

        var dune = gaps.Single(g => g.Name == "Dune");
        Assert.Equal("bibliography:" + AuthorKey + ":OL893415W", dune.Id);
        Assert.Equal(1965, dune.Year);
    }

    [Fact]
    public void Build_SkipsOwnedWorksAndHonorsCap()
    {
        var works = LoadWorks();
        var owned = IndexWith(OwnershipIndex.MakeKey(BaseItemKind.Book, OpenLibraryMapper.OpenLibraryProvider, "OL893415W"));

        var gaps = OpenLibraryMapper.Build(AuthorKey, "Frank Herbert", works, "owner-guid", owned, 2).ToList();

        Assert.DoesNotContain(gaps, g => g.Name == "Dune");
        Assert.Equal(2, gaps.Count);
    }
}
