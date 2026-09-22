using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class TodoDemandAggregatorTests
{
    private static readonly Guid Ann = Guid.NewGuid();
    private static readonly Guid Vic = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    [Fact]
    public void Build_WithNothing_ReturnsEmpty()
    {
        Assert.Empty(TodoDemandAggregator.Build([]));
    }

    [Fact]
    public void Build_FoldsTwoUsersRequestsForTheSameTmdbIdIntoOneRow()
    {
        var entries = new[]
        {
            Entry(Ann, "Ann", "collection:1:76341", "Mad Max 2", "76341"),
            Entry(Vic, "Vic", "recommendation:movie:76341", "Mad Max 2", "76341")
        };

        var row = Assert.Single(TodoDemandAggregator.Build(entries));

        Assert.Equal(2, row.RequestCount);
        Assert.Equal(2, row.OpenCount);
        Assert.Equal(new[] { "Ann", "Vic" }, row.RequestedBy);
        Assert.Equal(2, row.Entries.Count);
    }

    [Fact]
    public void Build_WithNoSharedIds_StaysSeparateRows()
    {
        var entries = new[]
        {
            Entry(Ann, "Ann", "a", "Movie A", "1"),
            Entry(Vic, "Vic", "b", "Movie B", "2")
        };

        var rows = TodoDemandAggregator.Build(entries);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.RequestCount));
    }

    [Fact]
    public void Build_MatchesTransitivelyThroughAPartiallyOverlappingEntry()
    {
        // Ann's entry carries only a TMDB id, Bob's only an IMDb id; they share nothing directly, but Vic's
        // carries both, so all three are one title. Mirrors GapTargetKey's own transitivity guarantee.
        var tmdbOnly = Entry(Ann, "Ann", "a", "Mad Max 2", "76341");
        var both = EntryWithIds(Vic, "Vic", "b", "Mad Max 2", new Dictionary<string, string> { ["Tmdb"] = "76341", ["Imdb"] = "tt0082694" });
        var imdbOnly = EntryWithIds(Bob, "Bob", "c", "Mad Max 2", new Dictionary<string, string> { ["Imdb"] = "tt0082694" });

        var row = Assert.Single(TodoDemandAggregator.Build([tmdbOnly, both, imdbOnly]));

        Assert.Equal(3, row.RequestCount);
        Assert.Equal("76341", row.ProviderIds["Tmdb"]);
        Assert.Equal("tt0082694", row.ProviderIds["Imdb"]);
    }

    [Fact]
    public void Build_DoesNotMatchAcrossItemKinds()
    {
        var movie = Entry(Ann, "Ann", "m", "Same Number", "1");
        var series = EntryWithIds(Vic, "Vic", "s", "Same Number", new Dictionary<string, string> { ["Tmdb"] = "1" }, kind: "Series");

        var rows = TodoDemandAggregator.Build([movie, series]);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Build_MatchesAlbumsByArtistAndTitleWhenIdsDoNotOverlap()
    {
        var musicBrainz = EntryWithIds(
            Ann, "Ann", "mb", "Kind of Blue", new Dictionary<string, string> { ["MusicBrainzReleaseGroup"] = "rg-1" }, kind: "MusicAlbum", creator: "Miles Davis");
        var discogs = EntryWithIds(
            Vic, "Vic", "dg", "Kind of Blue", new Dictionary<string, string> { ["Discogs"] = "r-9" }, kind: "MusicAlbum", creator: "Miles Davis");

        var row = Assert.Single(TodoDemandAggregator.Build([musicBrainz, discogs]));

        Assert.Equal(2, row.RequestCount);
    }

    [Fact]
    public void Build_SortsByOpenCountThenRequestCountThenName()
    {
        var oneOpenTwoRequests = new[]
        {
            Entry(Ann, "Ann", "a1", "Two Requests One Open", "1"),
            Done(Entry(Vic, "Vic", "a2", "Two Requests One Open", "1"))
        };
        var twoOpen = new[]
        {
            Entry(Ann, "Ann", "b1", "Two Open", "2"),
            Entry(Vic, "Vic", "b2", "Two Open", "2")
        };
        var oneOpenOneRequest = new[] { Entry(Bob, "Bob", "c1", "Alpha Solo", "3") };

        var rows = TodoDemandAggregator.Build(oneOpenTwoRequests.Concat(twoOpen).Concat(oneOpenOneRequest).ToList());

        // "Two Open" leads on open count; the other two are tied at one open request each, so
        // "Two Requests One Open" (two total requesters) outranks "Alpha Solo" (one).
        Assert.Equal(new[] { "Two Open", "Two Requests One Open", "Alpha Solo" }, rows.Select(r => r.Name));
    }

    [Fact]
    public void Build_MarkingOneRequestersEntryDoneDoesNotCloseTheOthers()
    {
        var entries = new[]
        {
            Done(Entry(Ann, "Ann", "a", "Title", "1")),
            Entry(Vic, "Vic", "b", "Title", "1")
        };

        var row = Assert.Single(TodoDemandAggregator.Build(entries));

        Assert.Equal(2, row.RequestCount);
        Assert.Equal(1, row.OpenCount);
    }

    [Fact]
    public void Build_UnionsProviderIdsAndDeduplicatesLinksAcrossMembers()
    {
        var withImdb = EntryWithIds(Ann, "Ann", "a", "Title", new Dictionary<string, string> { ["Tmdb"] = "1", ["Imdb"] = "tt1" });
        withImdb.Links = [new ExternalLink { Name = "TMDB", Url = "https://tmdb/1" }];
        var withoutImdb = Entry(Vic, "Vic", "b", "Title", "1");
        withoutImdb.Links = [new ExternalLink { Name = "TMDB", Url = "https://tmdb/1" }, new ExternalLink { Name = "IMDb", Url = "https://imdb/tt1" }];

        var row = Assert.Single(TodoDemandAggregator.Build([withImdb, withoutImdb]));

        Assert.Equal("tt1", row.ProviderIds["Imdb"]);
        Assert.Equal(2, row.Links.Count);
    }

    private static OwnedTodoEntry Entry(Guid ownerId, string ownerName, string id, string name, string tmdbId)
        => EntryWithIds(ownerId, ownerName, id, name, new Dictionary<string, string> { ["Tmdb"] = tmdbId });

    private static OwnedTodoEntry EntryWithIds(
        Guid ownerId, string ownerName, string id, string name, Dictionary<string, string> providerIds, string kind = "Movie", string? creator = null)
        => new()
        {
            Id = id,
            Name = name,
            DomainName = kind == "MusicAlbum" ? "Music" : "Movies",
            TargetKindName = kind,
            Creator = creator,
            ProviderIds = providerIds,
            AddedUtc = "2026-01-01T00:00:00Z",
            OwnerId = ownerId,
            OwnerName = ownerName
        };

    private static OwnedTodoEntry Done(OwnedTodoEntry entry)
    {
        entry.Done = true;
        return entry;
    }
}
