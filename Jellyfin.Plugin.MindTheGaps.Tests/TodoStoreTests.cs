using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class TodoStoreTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mtg-todo-" + Guid.NewGuid().ToString("N"));

    private static TodoStore Store(string dir) => new(NullLogger<TodoStore>.Instance, dir);

    private static GapItem Gap(string id, string name = "A Film", int? year = 1999)
        => new()
        {
            Id = id,
            Name = name,
            Year = year,
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
            var added = Store(dir).Add(new[] { Gap("m:1", "The Matrix", 1999) });
            Assert.Equal(1, added);

            // A fresh instance reads from disk (the previous one's in-memory cache is gone).
            var all = Store(dir).Load();

            Assert.Single(all);
            var entry = all[0];
            Assert.Equal("m:1", entry.Id);
            Assert.Equal("The Matrix", entry.Name);
            Assert.Equal(1999, entry.Year);
            Assert.Equal("Movies", entry.DomainName);
            Assert.Equal("Movie", entry.TargetKindName);
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
            Assert.Equal(2, store.Add(new[] { Gap("m:1"), Gap("m:2") }));

            // m:1 already present; only m:3 is new.
            Assert.Equal(1, store.Add(new[] { Gap("m:1"), Gap("m:3") }));

            Assert.Equal(3, Store(dir).Load().Count);
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
            store.Add(new[] { Gap("m:1"), Gap("m:2") });

            Assert.Equal(1, store.Remove("m:1"));
            Assert.Equal(0, store.Remove("m:1"));

            var all = Store(dir).Load();
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
            store.Add(new[] { Gap("m:1") });

            Assert.True(store.SetDone("m:1", true));
            var done = Store(dir).Load().Single();
            Assert.True(done.Done);
            Assert.NotNull(done.DoneUtc);

            Assert.True(store.SetDone("m:1", false));
            var undone = Store(dir).Load().Single();
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
            Assert.False(Store(dir).SetDone("nope", true));
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
            store.Add(new[] { Gap("m:1", "Old Title") });
            store.SetDone("m:1", true);
            var addedUtc = store.Load().Single().AddedUtc;

            // Re-add with refreshed fields (a later scan): the snapshot updates but the done state and the
            // original added timestamp are preserved, so the user does not lose progress.
            Assert.Equal(0, store.Add(new[] { Gap("m:1", "New Title") }));

            var entry = Store(dir).Load().Single();
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
}
