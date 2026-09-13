using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class WantToWatchServiceTests
{
    private static TodoEntry Entry(string id, string kind, string added, bool done = false, string? image = null) => new()
    {
        Id = id,
        Name = "Title " + id,
        Year = 2001,
        TargetKindName = kind,
        PatternName = "Recommendation",
        ProviderIds = new Dictionary<string, string> { ["Tmdb"] = id.Split(':').Last() },
        AddedUtc = added,
        Done = done,
        ImageUrl = image
    };

    [Fact]
    public void Cards_UndoneMoviesAndSeriesOnly_NewestAddedFirst_MarkedWanted()
    {
        var entries = new[]
        {
            Entry("recommendation:movie:1", "Movie", "2026-09-01T00:00:00Z"),
            Entry("recommendation:series:2", "Series", "2026-09-03T00:00:00Z", image: "https://image.tmdb.org/t/p/w500/x.jpg"),
            Entry("recommendation:movie:3", "Movie", "2026-09-02T00:00:00Z", done: true),
            Entry("discography:album:4", "MusicAlbum", "2026-09-04T00:00:00Z")
        };

        var cards = WantToWatchService.Cards(entries);

        Assert.Equal(["recommendation:series:2", "recommendation:movie:1"], cards.Select(c => c.GapId));
        Assert.All(cards, c => Assert.True(c.Wanted));
        Assert.Equal("Series", cards[0].Kind);
        Assert.Equal("https://image.tmdb.org/t/p/w500/x.jpg", cards[0].ImageUrl);
        Assert.Equal(2, cards[0].TmdbId);
    }

    [Fact]
    public void FromEntry_RebuildsAGapTheDetailAndSendPathsCanUse()
    {
        var entry = Entry("filmography:series:1408", "Series", "2026-09-01T00:00:00Z");
        entry.PatternName = "CreatorWorks";
        entry.Creator = "Bryan Cranston";
        entry.ReleaseDate = new DateTime(2004, 11, 16);

        var gap = WantToWatchService.FromEntry(entry)!;

        Assert.Equal("filmography:series:1408", gap.Id);
        Assert.Equal(BaseItemKind.Series, gap.TargetKind);
        Assert.Equal(MediaDomain.Shows, gap.Domain);
        Assert.Equal(GapPattern.CreatorWorks, gap.Pattern);
        Assert.Equal("1408", gap.ProviderIds["Tmdb"]);
        Assert.Equal(2001, gap.Year);
        Assert.False(gap.IsUpcoming);
        Assert.Null(WantToWatchService.FromEntry(Entry("x", "Book", "2026-09-01T00:00:00Z")));
    }

    [Fact]
    public void AddMarkRemove_RoundTripsThroughTheTodoStore()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mtg-want-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            // Only the todo-store paths are exercised here; the library-facing collaborators (the arrival
            // bridge) need a live ILibraryManager and are covered by the deployed instance, not unit tests.
            var service = new WantToWatchService(new TodoStore(NullLogger<TodoStore>.Instance, dir), null!, null!, null!, NullLogger<WantToWatchService>.Instance);
            var gap = new GapItem
            {
                Id = "recommendation:movie:275",
                Name = "Fargo",
                TargetKind = BaseItemKind.Movie,
                Pattern = GapPattern.Recommendation,
                ImageUrl = "https://image.tmdb.org/t/p/w500/f.jpg",
                ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "275" }
            };

            Assert.True(service.Add(gap));
            Assert.False(service.Add(gap));

            var card = MissingTitleBuilder.ToTitle(gap, null, null)!;
            service.Mark([card]);
            Assert.True(card.Wanted);
            Assert.Equal("https://image.tmdb.org/t/p/w500/f.jpg", service.FindGap(gap.Id)!.ImageUrl);

            Assert.True(service.Remove(gap.Id));
            Assert.False(service.Remove(gap.Id));
            service.Mark([card]);
            Assert.False(card.Wanted);
            Assert.Null(service.FindGap(gap.Id));
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SameTitleFromTwoSurfaces_IsOneEntry_MarkedAndRemovedAsOne()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mtg-want-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var service = new WantToWatchService(new TodoStore(NullLogger<TodoStore>.Instance, dir), null!, null!, null!, NullLogger<WantToWatchService>.Instance);
            var fromPerson = new GapItem { Id = "filmography:movie:1421903", Name = "Werwulf", TargetKind = BaseItemKind.Movie, Pattern = GapPattern.CreatorWorks, ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1421903" } };
            var fromSearch = new GapItem { Id = "recommendation:movie:1421903", Name = "Werwulf", TargetKind = BaseItemKind.Movie, Pattern = GapPattern.Recommendation, ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1421903" } };

            Assert.True(service.Add(fromPerson));
            Assert.False(service.Add(fromSearch));

            var searchCard = MissingTitleBuilder.ToTitle(fromSearch, null, null)!;
            service.Mark([searchCard]);
            Assert.True(searchCard.Wanted);

            Assert.True(service.RemoveMatching(fromSearch));
            Assert.Empty(service.WantedKeys());
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }
}
