using System;
using System.IO;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ScanCursorStoreTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mtg-cursor-" + Guid.NewGuid().ToString("N"));

    private static ScanCursorStore Store(string dir) => new(NullLogger<ScanCursorStore>.Instance, dir);

    [Fact]
    public void MarkScanned_StampsKeys_AndReloadsFromDisk()
    {
        var dir = TempDir();
        try
        {
            var before = DateTime.UtcNow;
            Store(dir).MarkScanned("Filmography", new[] { "a", "b" });
            var after = DateTime.UtcNow;

            var times = Store(dir).GetLastScanned("Filmography");
            Assert.Equal(2, times.Count);
            Assert.InRange(times["a"], before, after);
            Assert.InRange(times["b"], before, after);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MarkScanned_OverwritesEarlierStamp_ForRescannedKey()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.MarkScanned("Filmography", new[] { "a" });
            var first = store.GetLastScanned("Filmography")["a"];

            store.MarkScanned("Filmography", new[] { "a" });
            var second = store.GetLastScanned("Filmography")["a"];

            Assert.True(second >= first);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GetLastScanned_IsPerSource_AndUnknownSourceIsEmpty()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.MarkScanned("Filmography", new[] { "a" });
            store.MarkScanned("Recommendations", new[] { "x", "y" });

            Assert.Single(Store(dir).GetLastScanned("Filmography"));
            Assert.Equal(2, Store(dir).GetLastScanned("Recommendations").Count);
            Assert.Empty(Store(dir).GetLastScanned("Nope"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
