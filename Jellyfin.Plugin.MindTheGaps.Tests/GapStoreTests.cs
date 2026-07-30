using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class GapStoreTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mtg-store-" + Guid.NewGuid().ToString("N"));

    private static GapStore Store(string dir) => new(NullLogger<GapStore>.Instance, dir);

    private static GapReport ReportWith(GapItem item) => new() { GeneratedUtc = DateTime.UtcNow, TotalGaps = 1, Items = new[] { item } };

    private static GapItem Gap(string id) => new() { Id = id, Name = id };

    private static GapItem SeriesGap(string id, string seriesKey) => new() { Id = id, Name = id, SourceItemId = seriesKey };

    [Fact]
    public void SaveAvailabilityMerge_WhenReportIsCurrent_PersistsIt()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            var enriched = Gap("g1");
            enriched.AvailabilityChecked = true;
            enriched.Availability = new[] { new AvailabilityOffer { Provider = "Netflix" } };
            var report = ReportWith(enriched);

            store.Save(report);
            store.SaveAvailabilityMerge(report, throttle: false);

            var loaded = store.Load();
            Assert.Same(report, loaded);
            Assert.True(loaded.Items[0].AvailabilityChecked);
            Assert.Single(loaded.Items[0].Availability);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SaveAvailabilityMerge_WhenScanReplacedCache_MergesEnrichmentIntoNewReport()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);

            // The pass loaded this report and enriched g1.
            var passGap = Gap("g1");
            passGap.AvailabilityChecked = true;
            passGap.Availability = new[] { new AvailabilityOffer { Provider = "Netflix" } };
            passGap.ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1", ["Imdb"] = "tt1" };
            var passReport = ReportWith(passGap);
            store.Save(passReport);

            // A scan finished mid-pass and replaced the cache with a fresh report (same gap, no enrichment).
            var scanGap = Gap("g1");
            var scanReport = ReportWith(scanGap);
            store.Save(scanReport);

            // The pass now saves its (older) captured report. It must land on the new report, not clobber it.
            store.SaveAvailabilityMerge(passReport, throttle: false);

            var loaded = store.Load();
            Assert.Same(scanReport, loaded);
            Assert.True(loaded.Items[0].AvailabilityChecked);
            Assert.Single(loaded.Items[0].Availability);
            Assert.Equal("tt1", loaded.Items[0].ProviderIds["Imdb"]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LoadSnapshot_ReturnsAnIndependentListDecoupledFromLaterSaves()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Save(ReportWith(Gap("g1")));

            var snapshot = store.LoadSnapshot();

            // A scan replaces the cache; the snapshot already taken must not change.
            store.Save(new GapReport { Items = new[] { Gap("a"), Gap("b") } });

            Assert.Single(snapshot.Items);
            Assert.Equal("g1", snapshot.Items[0].Id);
            Assert.NotSame(store.Load().Items, snapshot.Items);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task LoadSnapshot_UnderConcurrentMerge_DoesNotThrow()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            var items = new List<GapItem>();
            for (var i = 0; i < 500; i++) { items.Add(Gap("g" + i.ToString(CultureInfo.InvariantCulture))); }
            var report = new GapReport { TotalGaps = items.Count, Items = items.ToArray() };
            store.Save(report);

            // One thread keeps merging enrichment onto the cached items while another keeps snapshotting and
            // enumerating. The snapshot decouples the list, so enumeration cannot observe a structural change.
            using var done = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var writer = Task.Run(() =>
            {
                while (!done.IsCancellationRequested) { store.SaveAvailabilityMerge(report, throttle: true); }
            });

            for (var r = 0; r < 200; r++)
            {
                var snap = store.LoadSnapshot();
                var n = 0;
                foreach (var item in snap.Items) { n += item.Id.Length; }
                Assert.True(n >= 0);
            }

            await writer;
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SaveAvailabilityMerge_DoesNotResurrectGapsAbsentFromNewReport()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);

            var passReport = ReportWith(Gap("gone"));
            store.Save(passReport);

            // The new report no longer has "gone" (resolved or acquired since the pass started).
            var scanReport = ReportWith(Gap("kept"));
            store.Save(scanReport);

            store.SaveAvailabilityMerge(passReport, throttle: false);

            var loaded = store.Load();
            Assert.Single(loaded.Items);
            Assert.Equal("kept", loaded.Items[0].Id);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReplaceSourceGaps_ForASeries_SwapsOnlyThatSeriesContentGapsAndKeepsTheRest()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            var seriesId = Guid.NewGuid();
            var key = seriesId.ToString("N");
            var otherKey = Guid.NewGuid().ToString("N");

            store.Save(new GapReport
            {
                Items = new[]
                {
                    SeriesGap("seriescontent:" + key + ":s01e01", key),
                    SeriesGap("seriescontent:" + key + ":s01e02", key),       // a fix will resolve this one
                    SeriesGap("seriescontent:" + otherKey + ":s01e01", otherKey), // another series, untouched
                    Gap("movie:1")                                            // unrelated, untouched
                }
            });

            // The re-check now finds only E01 for this series (E02 was fixed).
            var fresh = new GapReport { Items = new[] { SeriesGap("seriescontent:" + key + ":s01e01", key) } };
            var updated = store.ReplaceSourceGaps(key, new[] { "seriescontent:" }, fresh);

            Assert.Contains(updated.Items, i => i.Id == "seriescontent:" + key + ":s01e01");
            Assert.DoesNotContain(updated.Items, i => i.Id == "seriescontent:" + key + ":s01e02");
            Assert.Contains(updated.Items, i => i.Id == "seriescontent:" + otherKey + ":s01e01");
            Assert.Contains(updated.Items, i => i.Id == "movie:1");
            Assert.Equal(3, updated.TotalGaps);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReplaceSourceGaps_SwapsOnlyTheClaimedPrefixesForThatOwner()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            var boxSet = Guid.NewGuid().ToString("N");

            store.Save(new GapReport
            {
                Items = new[]
                {
                    SeriesGap("collection:10:1", boxSet),      // re-checked away
                    SeriesGap("collection:10:2", boxSet),      // re-checked, still missing
                    SeriesGap("recommendation:10:9", boxSet),  // same owner, another source: untouched
                    SeriesGap("collection:11:1", Guid.NewGuid().ToString("N")) // another owner: untouched
                }
            });

            var fresh = new GapReport { Items = new[] { SeriesGap("collection:10:2", boxSet) } };
            var updated = store.ReplaceSourceGaps(boxSet, new[] { "collection:" }, fresh);

            Assert.DoesNotContain(updated.Items, i => i.Id == "collection:10:1");
            Assert.Contains(updated.Items, i => i.Id == "collection:10:2");
            Assert.Contains(updated.Items, i => i.Id == "recommendation:10:9");
            Assert.Contains(updated.Items, i => i.Id == "collection:11:1");
            Assert.Equal(3, updated.TotalGaps);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReplaceSourceGaps_WithSeveralPrefixes_SwapsEachClaimingSourcesGaps()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            var artist = Guid.NewGuid().ToString("N");

            // An artist is claimed by both music sources at once, so a re-check swaps both prefixes.
            store.Save(new GapReport
            {
                Items = new[]
                {
                    SeriesGap("discography:mb:1", artist),
                    SeriesGap("discogsartist:99:1", artist),
                    SeriesGap("artistworks:mb:1", artist)
                }
            });

            var fresh = new GapReport { Items = new[] { SeriesGap("discography:mb:2", artist) } };
            var updated = store.ReplaceSourceGaps(artist, new[] { "discography:", "discogsartist:" }, fresh);

            Assert.DoesNotContain(updated.Items, i => i.Id == "discography:mb:1");
            Assert.DoesNotContain(updated.Items, i => i.Id == "discogsartist:99:1");
            Assert.Contains(updated.Items, i => i.Id == "discography:mb:2");
            Assert.Contains(updated.Items, i => i.Id == "artistworks:mb:1");
            Assert.Equal(2, updated.TotalGaps);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RemoveGaps_DropsOnlyTheNamedIdsAndPreservesTheScanStamp()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            var generated = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            store.Save(new GapReport
            {
                GeneratedUtc = generated,
                GeneratedVersion = "1.2.3.4",
                Items = new[] { Gap("a"), Gap("b"), Gap("c") }
            });

            var removed = store.RemoveGaps(new[] { "a", "c", "never-there" });

            var loaded = store.Load();
            Assert.Equal(2, removed);
            Assert.Single(loaded.Items);
            Assert.Equal("b", loaded.Items[0].Id);
            Assert.Equal(1, loaded.TotalGaps);

            // A verify is a partial update, so it must not look like a fresh scan (which would clear
            // the post-upgrade rescan nudge).
            Assert.Equal(generated, loaded.GeneratedUtc);
            Assert.Equal("1.2.3.4", loaded.GeneratedVersion);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RemoveGaps_WithNoMatchingIds_LeavesTheReportAlone()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            var report = new GapReport { TotalGaps = 1, Items = new[] { Gap("a") } };
            store.Save(report);

            Assert.Equal(0, store.RemoveGaps(new[] { "nope" }));
            Assert.Equal(0, store.RemoveGaps(Array.Empty<string>()));
            Assert.Same(report, store.Load());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
