using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class NotInterestedFilterTests
{
    private static MissingTitle Card(string gapId, string kind, int tmdbId)
        => new() { GapId = gapId, Title = gapId, Kind = kind, TmdbId = tmdbId };

    [Fact]
    public void LeavesOutTheTitlesTheCallerHid_ByKindAndTmdbId_InOrder()
    {
        var cards = new[] { Card("a", "Movie", 1), Card("b", "Movie", 2), Card("c", "Series", 2), Card("d", "Movie", 3) };
        var hidden = new HashSet<string> { OwnershipIndex.MakeKey(BaseItemKind.Movie, ProviderIds.Tmdb, "2") };

        var shown = NotInterestedFilter.Without(cards, hidden);

        // A series sharing the movie's TMDB number is a different title.
        Assert.Equal(["a", "c", "d"], shown.Select(t => t.GapId));
    }

    [Fact]
    public void NothingHidden_HandsBackTheSameList()
    {
        var cards = new[] { Card("a", "Movie", 1) };

        Assert.Same(cards, NotInterestedFilter.Without(cards, new HashSet<string>()));
    }
}
