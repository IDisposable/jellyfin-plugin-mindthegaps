using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class AvailabilityRunnerTests
{
    private static GapItem Movie(string id, string tmdb, bool isChecked) => new()
    {
        Id = id,
        TargetKind = BaseItemKind.Movie,
        ProviderIds = new Dictionary<string, string> { ["Tmdb"] = tmdb },
        AvailabilityChecked = isChecked
    };

    private static GapItem Episode(string id, string watchTmdb, bool isChecked) => new()
    {
        Id = id,
        TargetKind = BaseItemKind.Episode,
        WatchTmdbId = watchTmdb,
        AvailabilityChecked = isChecked
    };

    [Fact]
    public void PendingTitleCount_CountsUncheckedWatchableTargets()
    {
        var report = new GapReport
        {
            Items = new[]
            {
                Movie("m1", "100", false),
                Movie("m2", "200", true),    // already checked, not pending
                Movie("m3", string.Empty, false) // no tmdb id, not watchable
            }
        };

        Assert.Equal(1, AvailabilityRunner.PendingTitleCount(report));
    }

    [Fact]
    public void PendingTitleCount_CollapsesEpisodesOfOneSeriesToOneTitle()
    {
        // Every episode of one series shares the series' watch target, so the pass (and this count) treats
        // them as a single title to look up.
        var report = new GapReport
        {
            Items = new[]
            {
                Episode("s1e1", "555", false),
                Episode("s1e2", "555", false),
                Episode("s1e3", "555", false),
                Episode("other", "777", false)
            }
        };

        Assert.Equal(2, AvailabilityRunner.PendingTitleCount(report));
    }

    [Fact]
    public void PendingTitleCount_ZeroWhenAllChecked()
    {
        var report = new GapReport
        {
            Items = new[] { Movie("m1", "100", true), Episode("s1e1", "555", true) }
        };

        Assert.Equal(0, AvailabilityRunner.PendingTitleCount(report));
    }
}
