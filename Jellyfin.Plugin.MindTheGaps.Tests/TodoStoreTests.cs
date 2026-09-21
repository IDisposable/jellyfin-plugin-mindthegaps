using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class TodoStoreTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mtg-todo-" + Guid.NewGuid().ToString("N"));

    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static readonly JsonSerializerOptions _legacyJson = new(JsonSerializerDefaults.Web);

    private static TodoStore Store(string dir) => new(NullLogger<TodoStore>.Instance, dir);

    private static GapItem Gap(string id, string name = "A Film", int? year = 1999)
        => new()
        {
            Id = id,
            Name = name,
            Year = year,
            Pattern = GapPattern.CreatorWorks,
            Domain = MediaDomain.Movies,
            TargetKind = BaseItemKind.Movie,
            SourceItemName = "Some Creator",
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" },
            Links = new[] { new ExternalLink("TMDB", "https://example.test/603") }
        };

    [Fact]
    public void Add_Snapshots_AndReloadsFromDisk()
    {
        var dir = TempDir();
        try
        {
            var added = Store(dir).Add(Owner, new[] { Gap("m:1", "The Matrix", 1999) });
            Assert.Equal(1, added);

            // A fresh instance reads from disk (the previous one's in-memory cache is gone).
            var all = Store(dir).Load(Owner);

            Assert.Single(all);
            var entry = all[0];
            Assert.Equal("m:1", entry.Id);
            Assert.Equal("The Matrix", entry.Name);
            Assert.Equal(1999, entry.Year);
            Assert.Equal("Movies", entry.DomainName);
            Assert.Equal("Movie", entry.TargetKindName);
            Assert.Equal("CreatorWorks", entry.PatternName);
            Assert.Equal("Some Creator", entry.Creator);
            Assert.Equal("603", entry.ProviderIds["Tmdb"]);
            Assert.Single(entry.Links);
            Assert.Equal("TMDB", entry.Links[0].Name);
            Assert.False(entry.Done);
            Assert.Null(entry.DoneUtc);
            Assert.NotEqual(string.Empty, entry.AddedUtc);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Add_CountsOnlyNewlyAdded_AndUpsertsById()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            Assert.Equal(2, store.Add(Owner, new[] { Gap("m:1"), Gap("m:2") }));

            // m:1 already present; only m:3 is new.
            Assert.Equal(1, store.Add(Owner, new[] { Gap("m:1"), Gap("m:3") }));

            Assert.Equal(3, Store(dir).Load(Owner).Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Remove_DropsOnlyThatEntry()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Owner, new[] { Gap("m:1"), Gap("m:2") });

            Assert.Equal(1, store.Remove(Owner, "m:1"));
            Assert.Equal(0, store.Remove(Owner, "m:1"));

            var all = Store(dir).Load(Owner);
            Assert.Single(all);
            Assert.Equal("m:2", all[0].Id);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SetDone_StampsAndClearsTimestamp_AndPersists()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Owner, new[] { Gap("m:1") });

            Assert.True(store.SetDone(Owner, "m:1", true));
            var done = Store(dir).Load(Owner).Single();
            Assert.True(done.Done);
            Assert.NotNull(done.DoneUtc);

            Assert.True(store.SetDone(Owner, "m:1", false));
            var undone = Store(dir).Load(Owner).Single();
            Assert.False(undone.Done);
            Assert.Null(undone.DoneUtc);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SetDone_UnknownId_ReturnsFalse()
    {
        var dir = TempDir();
        try
        {
            Assert.False(Store(dir).SetDone(Owner, "nope", true));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Add_ReAdd_PreservesDoneStateAndAddedTimestamp()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Owner, new[] { Gap("m:1", "Old Title") });
            store.SetDone(Owner, "m:1", true);
            var addedUtc = store.Load(Owner).Single().AddedUtc;

            // Re-add with refreshed fields (a later scan): the snapshot updates but the done state and the
            // original added timestamp are preserved, so the user does not lose progress.
            Assert.Equal(0, store.Add(Owner, new[] { Gap("m:1", "New Title") }));

            var entry = Store(dir).Load(Owner).Single();
            Assert.Equal("New Title", entry.Name);
            Assert.True(entry.Done);
            Assert.NotNull(entry.DoneUtc);
            Assert.Equal(addedUtc, entry.AddedUtc);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReconcileDone_AppliesEveryStateAndReportsWhatChanged()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Owner, new[] { Gap("a") });
            store.Add(Owner, new[] { Gap("b") });
            store.Add(Owner, new[] { Gap("c") });
            store.SetDone(Owner, "b", true);

            // a goes done, b stays done, c stays outstanding: only a moved.
            var changed = store.ReconcileDone(Owner, new Dictionary<string, bool>
            {
                ["a"] = true,
                ["b"] = true,
                ["c"] = false,
                ["missing"] = true
            });

            var byId = store.Load(Owner).ToDictionary(e => e.Id, StringComparer.Ordinal);
            Assert.Equal(1, changed);
            Assert.True(byId["a"].Done);
            Assert.True(byId["b"].Done);
            Assert.False(byId["c"].Done);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReconcileDone_ClearsAnEntryWhoseTitleHasLeftTheLibrary()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Owner, new[] { Gap("a") });
            store.SetDone(Owner, "a", true);

            Assert.Equal(1, store.ReconcileDone(Owner, new Dictionary<string, bool> { ["a"] = false }));

            var entry = Assert.Single(store.Load(Owner));
            Assert.False(entry.Done);
            Assert.Null(entry.DoneUtc);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReconcileDone_WithNothingToChange_ReportsZero()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Owner, new[] { Gap("a") });

            Assert.Equal(0, store.ReconcileDone(Owner, new Dictionary<string, bool> { ["a"] = false }));
            Assert.Equal(0, store.ReconcileDone(Owner, new Dictionary<string, bool>()));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EachUserHasAListOfTheirOwn()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Owner, new[] { Gap("m:1"), Gap("m:2") });
            store.Add(Other, new[] { Gap("m:3") });

            Assert.Equal(new[] { "m:1", "m:2" }, Store(dir).Load(Owner).Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal));
            Assert.Equal("m:3", Assert.Single(Store(dir).Load(Other)).Id);
            Assert.Equal(1, store.Remove(Owner, "m:1"));
            Assert.Single(store.Load(Other));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EachListIsItsOwnFileNamedForItsUser()
    {
        var dir = TempDir();
        try
        {
            Store(dir).Add(Owner, new[] { Gap("m:1") });

            Assert.True(File.Exists(Path.Combine(dir, "watchlists", Owner.ToString("N") + ".json")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ANoUserRequestIsRejected()
    {
        var store = Store(TempDir());

        Assert.Throws<ArgumentException>(() => store.Add(Guid.Empty, new[] { Gap("m:1") }));
        Assert.Throws<ArgumentException>(() => store.Load(Guid.Empty));
        Assert.Throws<ArgumentException>(() => store.AdoptLegacy(Guid.Empty));
    }

    [Fact]
    public void AdoptLegacy_MovesTheServerWideListToTheAdministrator_AndDeletesTheOldFile()
    {
        var dir = TempDir();
        try
        {
            var legacy = Path.Combine(dir, "todos.json");
            LegacyList(dir, Gap("old:1", "Old One"), Gap("old:2", "Old Two"));

            var adopted = Store(dir).AdoptLegacy(Owner);

            Assert.Equal(2, adopted);
            Assert.False(File.Exists(legacy));
            Assert.Equal(new[] { "old:1", "old:2" }, Store(dir).Load(Owner).Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal));
            Assert.Empty(Store(dir).Load(Other));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AdoptLegacy_KeepsWhatTheAdministratorAlreadyHas_AndRunsOnce()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Owner, new[] { Gap("m:1", "Mine") });
            LegacyList(dir, Gap("m:1", "Theirs"), Gap("m:2"));

            Assert.Equal(1, store.AdoptLegacy(Owner));
            Assert.Equal("Mine", store.Load(Owner).Single(e => e.Id == "m:1").Name);

            // A later administrator gets nothing: the file is gone.
            Assert.Equal(0, store.AdoptLegacy(Other));
            Assert.Empty(store.Load(Other));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AdoptLegacy_WithNoOldFile_DoesNothing()
    {
        var dir = TempDir();
        try
        {
            Assert.Equal(0, Store(dir).AdoptLegacy(Owner));
            Assert.Empty(Store(dir).Load(Owner));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AdoptLegacy_LeavesAFileItCannotReadInPlace()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            var legacy = Path.Combine(dir, "todos.json");
            File.WriteAllText(legacy, "{ not json");

            Assert.Equal(0, Store(dir).AdoptLegacy(Owner));
            Assert.True(File.Exists(legacy));
            Assert.Empty(Store(dir).Load(Owner));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Delete_RemovesAUsersListOnly()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Owner, new[] { Gap("m:1") });
            store.Add(Other, new[] { Gap("m:2") });

            Assert.True(store.Delete(Owner));
            Assert.False(store.Delete(Owner));

            Assert.Empty(Store(dir).Load(Owner));
            Assert.Single(Store(dir).Load(Other));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Prune_DeletesTheListsOfUsersThatAreGone_AndLeavesTheRest()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(Owner, new[] { Gap("m:1") });
            store.Add(Other, new[] { Gap("m:2") });
            var stray = Path.Combine(dir, "watchlists", "notes.json");
            File.WriteAllText(stray, "{}");

            var deleted = store.Prune(id => id == Owner);

            Assert.Equal(1, deleted);
            Assert.Single(Store(dir).Load(Owner));
            Assert.Empty(Store(dir).Load(Other));
            Assert.True(File.Exists(stray));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Prune_WithNoListsYetDoesNothing()
        => Assert.Equal(0, Store(TempDir()).Prune(_ => false));

    private static void LegacyList(string dir, params GapItem[] gaps)
    {
        Directory.CreateDirectory(dir);
        var map = gaps.ToDictionary(g => g.Id, g => new TodoEntry { Id = g.Id, Name = g.Name, TargetKindName = "Movie", DomainName = "Movies", AddedUtc = "2026-01-01T00:00:00.0000000Z" });
        File.WriteAllText(Path.Combine(dir, "todos.json"), JsonSerializer.Serialize(map, _legacyJson));
    }

    [Fact]
    public void ListOwners_NamesTheUsersWithAtLeastOneEntry()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            Assert.Empty(store.ListOwners());

            store.Add(Owner, new[] { Gap("m:1") });
            store.Add(Other, new[] { Gap("m:2") });
            store.Remove(Other, "m:2");

            Assert.Equal(Owner, Assert.Single(Store(dir).ListOwners()));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
