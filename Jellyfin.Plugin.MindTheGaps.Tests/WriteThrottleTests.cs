using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The writes nothing waits on are paced: due after the longer of two minutes and twenty times the last write,
// with whatever a paced save skipped carried along by the next write or written when the batch ends.
public sealed class WriteThrottleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mtg-throttle-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void IsDue_AfterTheFloor()
    {
        var clock = new Clock();
        var throttle = new WriteThrottle(clock);

        Assert.False(throttle.IsDue);
        clock.Advance(TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(1));
        Assert.False(throttle.IsDue);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(throttle.IsDue);
    }

    [Fact]
    public void Interval_StretchesToTwentyTimesASlowWrite()
    {
        var clock = new Clock();
        var throttle = new WriteThrottle(clock);

        throttle.Wrote(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(200), throttle.Interval);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(throttle.IsDue);
        clock.Advance(TimeSpan.FromSeconds(80));
        Assert.True(throttle.IsDue);
    }

    [Fact]
    public void Run_RestartsTheWait()
    {
        var clock = new Clock();
        var throttle = new WriteThrottle(clock);
        clock.Advance(TimeSpan.FromMinutes(5));

        throttle.Run(() => { });

        Assert.False(throttle.IsDue);
        Assert.Equal(WriteThrottle.Floor, throttle.Interval);
    }

    [Fact]
    public void PacedSwap_IsServedAtOnceAndWrittenByFlushPending()
    {
        var store = new GapStore(NullLogger<GapStore>.Instance, _dir);
        store.Save(Report(Gap("collection:1:1", "owner", MediaDomain.Movies)));
        var before = File.ReadAllText(MoviesFile);

        // A store just built has written within the floor, so a paced swap is not written yet.
        store.ReplaceSourceGaps("owner", ["collection:"], Report(Gap("collection:1:2", "owner", MediaDomain.Movies)), paced: true);

        Assert.Equal("collection:1:2", Assert.Single(store.Load().Items).Id);
        Assert.Equal(before, File.ReadAllText(MoviesFile));

        store.FlushPending();

        Assert.Contains("collection:1:2", File.ReadAllText(MoviesFile), StringComparison.Ordinal);
        Assert.DoesNotContain("collection:1:1", File.ReadAllText(MoviesFile), StringComparison.Ordinal);
    }

    [Fact]
    public void PacedSwap_IsCarriedAlongByTheNextWrite()
    {
        var store = new GapStore(NullLogger<GapStore>.Instance, _dir);
        store.Save(Report(Gap("collection:1:1", "owner", MediaDomain.Movies), Gap("seriescontent:x:s01e01", "series", MediaDomain.Shows)));

        store.ReplaceSourceGaps("owner", ["collection:"], Report(Gap("collection:1:2", "owner", MediaDomain.Movies)), paced: true);

        // A write of another domain also writes the movies swap the paced save skipped.
        store.RemoveGaps(["seriescontent:x:s01e01"]);

        var reloaded = new GapStore(NullLogger<GapStore>.Instance, _dir).Load();
        Assert.Equal("collection:1:2", Assert.Single(reloaded.Items).Id);
    }

    [Fact]
    public void PacedAvailabilitySave_WaitsForFlushPending()
    {
        var store = new GapStore(NullLogger<GapStore>.Instance, _dir);
        var report = Report(Gap("collection:1:1", "owner", MediaDomain.Movies));
        store.Save(report);

        report.Items[0].AvailabilityChecked = true;
        report.Items[0].Availability = [new AvailabilityOffer { Provider = "Netflix" }];
        store.SaveAvailabilityMerge(report, throttle: true);
        Assert.DoesNotContain("Netflix", File.ReadAllText(MoviesFile), StringComparison.Ordinal);

        store.FlushPending();
        Assert.Contains("Netflix", File.ReadAllText(MoviesFile), StringComparison.Ordinal);
    }

    private string MoviesFile => Path.Combine(_dir, "gaps-movies.json");

    private static GapItem Gap(string id, string owner, MediaDomain domain) => new() { Id = id, Name = id, SourceItemId = owner, Domain = domain };

    private static GapReport Report(params GapItem[] items) => new() { GeneratedUtc = DateTime.UtcNow, TotalGaps = items.Length, Items = items.ToList() };

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
