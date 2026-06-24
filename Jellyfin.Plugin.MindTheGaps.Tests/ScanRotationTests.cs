using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class ScanRotationTests
{
    [Fact]
    public void OrderByStalest_PutsNeverScannedFirstThenOldest()
    {
        var lastScanned = new Dictionary<string, DateTime>
        {
            ["recent"] = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            ["old"] = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var candidates = new[] { "recent", "old", "never" };

        var ordered = candidates.OrderByStalest(lastScanned, c => c).ToList();

        Assert.Equal(new[] { "never", "old", "recent" }, ordered);
    }
}
