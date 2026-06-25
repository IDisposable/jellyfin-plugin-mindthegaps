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

// Real response captured from the keyless, public OpenLibrary subjects endpoint:
//   curl -s "https://openlibrary.org/subjects/science_fiction.json?limit=12" -o openlibrary_subject.json
// The capture carries the cases the mapper handles: every work has a title and a "/works/OLxxxW" key, each
// has an author for the owned-by-name fallback, and one (The Invisible Man) is listed with first_publish_year
// 0, so the year-guard path is exercised on real data.
public class OpenLibrarySubjectTests
{
    private const string Subject = "science_fiction";
    private const string SubjectName = "science fiction";

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

    private static IReadOnlyList<OpenLibrarySubjectWork> LoadWorks()
    {
        var response = JsonSerializer.Deserialize<OpenLibrarySubjectResponse>(
            TestData.Read("openlibrary_subject.json"),
            Options);
        Assert.NotNull(response);
        Assert.Equal(SubjectName, response!.Name);
        Assert.NotNull(response.Works);
        return response.Works!;
    }

    private static IReadOnlyList<GapItem> Build(OwnershipIndex ownership, int cap = 200)
        => OpenLibrarySubjectMapper.Build(Subject, SubjectName, LoadWorks(), ownership, cap).ToList();

    [Fact]
    public void Build_EmitsSetCompletionGaps_ForEveryWork()
    {
        var gaps = Build(IndexWith());

        // The capture holds twelve distinct works, each with a title and a work id, so all twelve become gaps.
        Assert.Equal(12, gaps.Count);
        Assert.All(gaps, g => Assert.Equal(GapPattern.SetCompletion, g.Pattern));
        Assert.All(gaps, g => Assert.Equal(MediaDomain.Books, g.Domain));
        Assert.All(gaps, g => Assert.Equal(BaseItemKind.Book, g.TargetKind));
        Assert.All(gaps, g => Assert.Equal(SubjectName, g.SourceItemName));
        Assert.All(gaps, g => Assert.Equal("Subject", g.SourceItemType));

        var nineteen = gaps.Single(g => g.Name == "Nineteen Eighty-Four");
        Assert.Equal("openlibrarysubject:science_fiction:OL1168083W", nineteen.Id);
        Assert.Equal("OL1168083W", nineteen.ProviderIds[ProviderIds.OpenLibrary]);
        Assert.Equal(1949, nineteen.Year);

        // The Invisible Man is captured with first_publish_year 0, so it still maps but carries no year.
        var invisible = gaps.Single(g => g.Name == "The Invisible Man");
        Assert.Null(invisible.Year);
    }

    [Fact]
    public void Build_SkipsWorkOwnedByOpenLibraryId()
    {
        var owned = IndexWith(OwnershipIndex.MakeKey(BaseItemKind.Book, ProviderIds.OpenLibrary, "OL52267W"));

        var gaps = Build(owned);

        Assert.DoesNotContain(gaps, g => g.Name == "The Time Machine");
        Assert.Equal(11, gaps.Count);
    }

    [Fact]
    public void Build_SkipsWorkOwnedByName()
    {
        // The library holds the book under a different work key (no OpenLibrary id match); the author-and-title
        // name match still recognizes it as owned.
        var owned = IndexWith(OwnershipIndex.MakeKey(
            BaseItemKind.Book,
            OwnershipIndex.NameKeyProvider,
            OwnershipIndex.NameKey("George Orwell", "Nineteen Eighty-Four")));

        var gaps = Build(owned);

        Assert.DoesNotContain(gaps, g => g.Name == "Nineteen Eighty-Four");
        Assert.Equal(11, gaps.Count);
    }

    [Fact]
    public void Build_HonorsCap()
    {
        Assert.Single(Build(IndexWith(), cap: 1));
    }
}
