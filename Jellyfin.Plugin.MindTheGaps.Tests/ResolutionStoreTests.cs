using System;
using System.IO;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ResolutionStoreTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mtg-res-" + Guid.NewGuid().ToString("N"));

    private static ResolutionStore Store(string dir) => new(NullLogger<ResolutionStore>.Instance, dir);

    [Fact]
    public void Resolve_Persists_AndReloadsFromDisk()
    {
        var dir = TempDir();
        try
        {
            Store(dir).Resolve("ep:1", "combined into S01E01");
            Store(dir).Resolve("ep:2", null);

            // A fresh instance reads from disk (the previous one's in-memory cache is gone).
            var all = Store(dir).GetAll();

            Assert.Equal(2, all.Count);
            Assert.Equal("combined into S01E01", all["ep:1"].Note);
            Assert.Equal(string.Empty, all["ep:2"].Note);
            Assert.NotEqual(default, all["ep:1"].ResolvedUtc);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Clear_Removes_OnlyThatGap()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Resolve("ep:1", "a");
            store.Resolve("ep:2", "b");

            store.Clear("ep:1");

            var all = Store(dir).GetAll();
            Assert.False(all.ContainsKey("ep:1"));
            Assert.True(all.ContainsKey("ep:2"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Resolve_EmptyId_IsIgnored()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Resolve(string.Empty, "x");

            Assert.Empty(store.GetAll());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Resolve_SanitizesNote_StripsControlCharsTrimsAndCaps()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Resolve("ep:1", "  has\tcontrol\nchars  ");
            Assert.Equal("hascontrolchars", store.GetAll()["ep:1"].Note);

            store.Resolve("ep:2", new string('x', 600));
            Assert.Equal(100, store.GetAll()["ep:2"].Note.Length);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetAll_ReturnsACopy_NotTheLiveMap()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Resolve("ep:1", "a");

            var snapshot = store.GetAll();
            store.Clear("ep:1");

            // The earlier snapshot is unaffected by the later clear.
            Assert.True(snapshot.ContainsKey("ep:1"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Resolve_DefaultKind_IsNull_SoItIsOmitted()
    {
        var dir = TempDir();
        try
        {
            Store(dir).Resolve("ep:1", "note");
            Assert.Null(Store(dir).GetAll()["ep:1"].Kind);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SetState_NotInterested_AndSnooze_PersistKindAndDate()
    {
        var dir = TempDir();
        try
        {
            var until = new DateTime(2027, 5, 1, 0, 0, 0, DateTimeKind.Utc);
            Store(dir).SetState("m:1", GapResolution.NotInterested, "nope", null);
            Store(dir).SetState("m:2", GapResolution.Snoozed, null, until);

            var all = Store(dir).GetAll();
            Assert.Equal(GapResolution.NotInterested, all["m:1"].Kind);
            Assert.Null(all["m:1"].SnoozedUntil);
            Assert.Equal(GapResolution.Snoozed, all["m:2"].Kind);
            Assert.Equal(until, all["m:2"].SnoozedUntil);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
