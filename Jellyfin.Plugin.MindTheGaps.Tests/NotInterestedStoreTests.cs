using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// A user's "not interested" is about a title, not a gap: the same film is a different gap on a person page, a
// studio page and the home row, and saying it once has to hide it on all of them. And it is the user's alone.
public class NotInterestedStoreTests
{
    private static readonly Guid Ann = Guid.NewGuid();
    private static readonly Guid Vic = Guid.NewGuid();

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mtg-notinterested-" + Guid.NewGuid().ToString("N"));

    private static NotInterestedStore Store(string dir) => new(NullLogger<NotInterestedStore>.Instance, dir);

    private static GapItem Film(string gapId, string tmdbId, string? imdbId = null)
    {
        var ids = new Dictionary<string, string> { ["Tmdb"] = tmdbId };
        if (imdbId is not null)
        {
            ids["Imdb"] = imdbId;
        }

        return new GapItem
        {
            Id = gapId,
            Name = "Film " + tmdbId,
            Year = 2001,
            Pattern = GapPattern.CreatorWorks,
            Domain = MediaDomain.Movies,
            TargetKind = BaseItemKind.Movie,
            SourceItemName = "A Director",
            ImageUrl = "https://image.tmdb.org/t/p/w342/poster.jpg",
            ProviderIds = ids
        };
    }

    [Fact]
    public void ATitleIsHiddenByItsIdentity_SoItMatchesUnderAnyGapId()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            Assert.True(store.Add(Ann, Film("person:1:603", "603")));

            var (keys, count) = Store(dir).Keys(Ann);

            Assert.Equal(1, count);
            Assert.Contains(OwnershipIndex.MakeKey(BaseItemKind.Movie, ProviderIds.Tmdb, "603"), keys);
            Assert.DoesNotContain(OwnershipIndex.MakeKey(BaseItemKind.Series, ProviderIds.Tmdb, "603"), keys);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void TheSameTitleUnderAnotherGapId_IsNotAddedTwice()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            Assert.True(store.Add(Ann, Film("person:1:603", "603")));

            Assert.False(store.Add(Ann, Film("studio:9:603", "603", "tt0133093")));
            Assert.False(store.Add(Ann, Film("person:1:603", "603")));
            Assert.Single(store.Load(Ann));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void UndoingFromACard_GoesByTheTitle_WhicheverGapItWasHiddenFrom()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Ann, Film("person:1:603", "603"));
            store.Add(Ann, Film("person:1:604", "604"));

            var removed = store.RemoveMatching(Ann, GapTargetKey.For(Film("home:603", "603")).ToList());

            Assert.Equal(1, removed);
            Assert.Equal(["person:1:604"], Store(dir).Load(Ann).Select(e => e.Id));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void TheListShowsWhatItNeedsToRestoreFrom_AndRestoresById()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Ann, Film("person:1:603", "603"));

            var entry = Assert.Single(Store(dir).Load(Ann));
            Assert.Equal("Film 603", entry.Name);
            Assert.Equal(2001, entry.Year);
            Assert.Equal("Movie", entry.TargetKindName);
            Assert.Equal("https://image.tmdb.org/t/p/w342/poster.jpg", entry.ImageUrl);
            Assert.False(string.IsNullOrEmpty(entry.AddedUtc));

            Assert.Equal(1, store.Remove(Ann, entry.Id));
            Assert.Equal(0, store.Remove(Ann, entry.Id));
            Assert.Empty(store.Load(Ann));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void EachUsersListIsTheirOwn()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Ann, Film("person:1:603", "603"));

            Assert.Equal(0, store.Keys(Vic).Count);
            Assert.Empty(store.Load(Vic));
            Assert.Equal(0, store.Remove(Vic, "person:1:603"));
            Assert.Single(store.Load(Ann));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void ClearingEmptiesOnlyThatUsersList()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Ann, Film("person:1:603", "603"));
            store.Add(Ann, Film("person:1:604", "604"));
            store.Add(Vic, Film("person:1:603", "603"));

            Assert.Equal(2, store.Clear(Ann));
            Assert.Equal(0, store.Clear(Ann));

            Assert.Empty(Store(dir).Load(Ann));
            Assert.Equal(0, Store(dir).Keys(Ann).Count);
            Assert.Single(Store(dir).Load(Vic));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void AListBelongsToAUser()
    {
        var store = Store(TempDir());

        Assert.Throws<ArgumentException>(() => store.Add(Guid.Empty, Film("person:1:603", "603")));
        Assert.Throws<ArgumentException>(() => store.Load(Guid.Empty));
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
