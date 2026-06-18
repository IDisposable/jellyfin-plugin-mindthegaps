using System;
using System.Collections.Generic;
using System.IO;
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
}
