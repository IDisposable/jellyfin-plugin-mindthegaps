using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class WantToWatchTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mtg-want-" + Guid.NewGuid().ToString("N"));

    private static TodoStore Store(string dir) => new(NullLogger<TodoStore>.Instance, dir);

    private static GapItem Gap(string id, string name, string tmdb, BaseItemKind kind = BaseItemKind.Movie) => new()
    {
        Id = id,
        Name = name,
        TargetKind = kind,
        ProviderIds = new Dictionary<string, string> { ["Tmdb"] = tmdb }
    };

    private static TodoEntry Entry(string id, string name, string tmdb, string kind = "Movie", bool done = false, string added = "2026-01-01T00:00:00Z", DateTime? released = null) => new()
    {
        Id = id,
        Name = name,
        Year = 1999,
        TargetKindName = kind,
        DomainName = kind == "Series" ? "Shows" : "Movies",
        ProviderIds = new Dictionary<string, string> { ["Tmdb"] = tmdb },
        Done = done,
        AddedUtc = added,
        ReleaseDate = released,
        ImageUrl = "https://image.tmdb.org/t/p/w185/x.jpg"
    };

    private static OwnershipIndex Owning(BaseItemKind kind, string tmdb)
        => new(new HashSet<string> { OwnershipIndex.MakeKey(kind, "Tmdb", tmdb) });

    [Fact]
    public void TodoKeys_IdentifyATitleByItsKindAndProviderIds()
    {
        var keys = TodoKeys.For(Entry("a", "Heat", "949")).ToList();

        Assert.Equal(OwnershipIndex.MakeKey(BaseItemKind.Movie, "Tmdb", "949"), Assert.Single(keys));
        Assert.Empty(TodoKeys.For(Entry("a", "Heat", "949", kind: "NotAKind")));
    }

    [Fact]
    public void TodoKeys_MatchTheKeysOfTheGapThatCreatedTheEntry()
    {
        var store = Store(TempDir());
        store.Add(User, [Gap("filmography:movie:949", "Heat", "949")]);

        var wanted = store.WantedKeys(User);

        Assert.All(GapTargetKey.For(Gap("recommendation:movie:949", "Heat", "949")), key => Assert.Contains(key, wanted));
    }

    [Fact]
    public void WantedKeys_AreOnePerUser()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(User, [Gap("m:1", "Heat", "949")]);

            Assert.Single(store.WantedKeys(User));
            Assert.Empty(store.WantedKeys(Guid.NewGuid()));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RemoveMatching_TakesOffEveryEntryAboutTheTitle_WhateverIdItHas()
    {
        var dir = TempDir();
        try
        {
            var store = Store(dir);
            store.Add(User, [Gap("filmography:movie:949", "Heat", "949"), Gap("m:2", "Other", "1")]);

            var removed = store.RemoveMatching(User, GapTargetKey.For(Gap("recommendation:movie:949", "Heat", "949")).ToList());

            Assert.Equal(1, removed);
            Assert.Equal("m:2", Assert.Single(Store(dir).Load(User)).Id);
            Assert.Equal(0, store.RemoveMatching(User, []));
            Assert.Equal(0, store.RemoveMatching(User, ["movie|tmdb|999"]));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RemoveMatching_KeepsAMovieAndASeriesThatShareAnId()
    {
        var store = Store(TempDir());
        store.Add(User, [Gap("m:1", "Same Id Movie", "500"), Gap("s:1", "Same Id Series", "500", BaseItemKind.Series)]);

        store.RemoveMatching(User, GapTargetKey.For(Gap("x", "Same Id Movie", "500")).ToList());

        Assert.Equal("s:1", Assert.Single(store.Load(User)).Id);
    }

    [Fact]
    public void WantedMarker_MarksTheCardsWhoseTitleIsOnTheList()
    {
        var wanted = new HashSet<string> { OwnershipIndex.MakeKey(BaseItemKind.Movie, "Tmdb", "949"), OwnershipIndex.MakeKey(BaseItemKind.Series, "Tmdb", "1399") };
        var cards = new[]
        {
            new MissingTitle { GapId = "a", Kind = "Movie", TmdbId = 949 },
            new MissingTitle { GapId = "b", Kind = "Movie", TmdbId = 1399 },
            new MissingTitle { GapId = "c", Kind = "Series", TmdbId = 1399 }
        };

        WantedMarker.Mark(cards, wanted);

        Assert.Equal(new[] { true, false, true }, cards.Select(c => c.OnList));
    }

    [Fact]
    public void WorksMissingBuilder_MarksAWorkThatIsOnTheList()
    {
        var album = new GapItem
        {
            Id = "discography:x:y",
            Name = "Moving Pictures",
            TargetKind = BaseItemKind.MusicAlbum,
            ProviderIds = new Dictionary<string, string> { ["MusicBrainzReleaseGroup"] = "rg-1" }
        };
        var wanted = new HashSet<string> { OwnershipIndex.MakeKey(BaseItemKind.MusicAlbum, "MusicBrainzReleaseGroup", "rg-1") };

        Assert.True(WorksMissingBuilder.ToWork(album, wanted).OnList);
        Assert.False(WorksMissingBuilder.ToWork(album, new HashSet<string>()).OnList);
        Assert.False(WorksMissingBuilder.ToWork(album).OnList);
    }

    [Fact]
    public void WantedRow_ListsWhatIsStillWanted_NewestAddedFirst()
    {
        var row = WantedRowBuilder.Build(
            [
                Entry("old", "Older", "1", added: "2026-01-01T00:00:00Z"),
                Entry("new", "Newer", "2", added: "2026-03-01T00:00:00Z"),
                Entry("show", "A Show", "3", kind: "Series", added: "2026-02-01T00:00:00Z")
            ],
            new OwnershipIndex(new HashSet<string>()),
            10,
            Now);

        Assert.Equal(new[] { "new", "show", "old" }, row.Select(c => c.GapId));
        var series = row.Single(c => c.GapId == "show");
        Assert.Equal("Series", series.Kind);
        Assert.Equal(3, series.TmdbId);
        Assert.True(series.OnList);
        Assert.Equal("https://image.tmdb.org/t/p/w185/x.jpg", series.ImageUrl);
    }

    [Fact]
    public void WantedRow_LeavesOutWhatIsDone_WhatTheLibraryHolds_AndWhatItCannotShow()
    {
        var row = WantedRowBuilder.Build(
            [
                Entry("done", "Done", "1", done: true),
                Entry("owned", "Owned", "2"),
                Entry("album", "An Album", "3", kind: "MusicAlbum"),
                new TodoEntry { Id = "no-id", Name = "No Tmdb Id", TargetKindName = "Movie" },
                Entry("keep", "Keep", "4")
            ],
            Owning(BaseItemKind.Movie, "2"),
            10,
            Now);

        Assert.Equal("keep", Assert.Single(row).GapId);
    }

    [Fact]
    public void WantedRow_ATitleOwnedAsAMovieDoesNotHideASeriesWithTheSameId()
    {
        var row = WantedRowBuilder.Build([Entry("show", "A Show", "500", kind: "Series")], Owning(BaseItemKind.Movie, "500"), 10, Now);

        Assert.Single(row);
    }

    [Fact]
    public void WantedRow_MarksAnUnreleasedTitle_AndRespectsTheLimit()
    {
        var row = WantedRowBuilder.Build(
            [
                Entry("soon", "Soon", "1", added: "2026-03-01T00:00:00Z", released: Now.AddDays(30)),
                Entry("out", "Out", "2", added: "2026-02-01T00:00:00Z", released: Now.AddDays(-30)),
                Entry("more", "More", "3", added: "2026-01-01T00:00:00Z")
            ],
            new OwnershipIndex(new HashSet<string>()),
            2,
            Now);

        Assert.Equal(2, row.Count);
        Assert.True(row[0].Upcoming);
        Assert.False(row[1].Upcoming);
    }

    [Fact]
    public void WantedRow_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => WantedRowBuilder.Build(null!, new OwnershipIndex(new HashSet<string>()), 1, Now));
        Assert.Throws<ArgumentNullException>(() => WantedRowBuilder.Build([], null!, 1, Now));
    }

    [Fact]
    public void ARestrictedUserIsOneWithAParentalRatingLimit()
    {
        var open = new Jellyfin.Database.Implementations.Entities.User("open", "auth", "reset");
        var limited = new Jellyfin.Database.Implementations.Entities.User("limited", "auth", "reset") { MaxParentalRatingScore = 10 };

        Assert.False(WebUiAccess.IsRestricted(open));
        Assert.True(WebUiAccess.IsRestricted(limited));
        Assert.Throws<ArgumentNullException>(() => WebUiAccess.IsRestricted(null!));
    }
}
