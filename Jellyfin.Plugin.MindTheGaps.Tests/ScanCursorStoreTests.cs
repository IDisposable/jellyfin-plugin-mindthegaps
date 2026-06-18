using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ScanCursorStoreTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mtg-cursor-" + Guid.NewGuid().ToString("N"));

    private static ScanCursorStore Store(string dir) => new(NullLogger<ScanCursorStore>.Instance, dir);

    [Fact]
    public void MarkProcessed_AccumulatesAcrossCalls_AndReloadsFromDisk()
    {
        var dir = TempDir();
        try
        {
            Store(dir).MarkProcessed("Filmography", new[] { "a", "b" });
            Store(dir).MarkProcessed("Filmography", new[] { "b", "c" });

            var processed = Store(dir).GetProcessed("Filmography").OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "a", "b", "c" }, processed);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void StartNewCycle_ClearsOnlyThatSource()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.MarkProcessed("Filmography", new[] { "a" });
            store.MarkProcessed("Other", new[] { "x" });

            store.StartNewCycle("Filmography");

            Assert.Empty(Store(dir).GetProcessed("Filmography"));
            Assert.Single(Store(dir).GetProcessed("Other"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
