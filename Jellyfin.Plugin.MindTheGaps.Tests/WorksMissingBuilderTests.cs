using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class WorksMissingBuilderTests
{
    private static GapItem Album(string id, string name, DateTime? released = null) => new()
    {
        Id = id,
        Name = name,
        TargetKind = BaseItemKind.MusicAlbum,
        SourceItemName = "A Band",
        ReleaseDate = released,
        Year = released?.Year,
        ImageUrl = "https://coverartarchive.org/release-group/x/front-250",
        Links = new[] { new ExternalLink("MusicBrainz", "https://musicbrainz.org/release-group/x") }
    };

    private static GapItem Book(string id, string name, DateTime? released = null) => new()
    {
        Id = id,
        Name = name,
        TargetKind = BaseItemKind.Book,
        SourceItemName = "An Author",
        ReleaseDate = released,
        Year = released?.Year
    };

    [Fact]
    public void ToWork_CarriesTheAlbumsDisplayFieldsAndLinks()
    {
        var gap = Album("a1", "Moving Pictures", new DateTime(1981, 2, 12, 0, 0, 0, DateTimeKind.Utc));
        gap.IsUpcoming = true;

        var work = WorksMissingBuilder.ToWork(gap);

        Assert.Equal("a1", work.GapId);
        Assert.Equal("Moving Pictures", work.Title);
        Assert.Equal(1981, work.Year);
        Assert.Equal("MusicAlbum", work.Kind);
        Assert.Equal("A Band", work.Creator);
        Assert.Equal(gap.ImageUrl, work.ImageUrl);
        Assert.True(work.Upcoming);
        Assert.Equal("https://musicbrainz.org/release-group/x", Assert.Single(work.Links).Url);
    }

    [Fact]
    public void ToWork_NamesABookByItsKind()
    {
        var work = WorksMissingBuilder.ToWork(Book("b1", "Dune"));

        Assert.Equal("Book", work.Kind);
        Assert.Equal("An Author", work.Creator);
        Assert.Null(work.Year);
        Assert.Empty(work.Links);
    }

    [Fact]
    public void Build_OrdersNewestFirst_UndatedLast_TiesByTitle()
    {
        var works = WorksMissingBuilder.Build(
            new[]
            {
                Album("old", "Old", new DateTime(1975, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Album("undated", "Undated"),
                Album("new-b", "Beta", new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Album("new-a", "Alpha", new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            },
            _ => false);

        Assert.Equal(new[] { "new-a", "new-b", "old", "undated" }, works.Select(w => w.GapId));
    }

    [Fact]
    public void Build_DropsDismissedGaps()
    {
        var works = WorksMissingBuilder.Build(new[] { Album("keep", "Keep"), Album("gone", "Gone") }, id => id == "gone");

        Assert.Equal("keep", Assert.Single(works).GapId);
    }

    [Fact]
    public void Build_KeepsOnlyAlbumsAndBooks()
    {
        var movie = new GapItem { Id = "m", Name = "A Movie", TargetKind = BaseItemKind.Movie };

        var works = WorksMissingBuilder.Build(new[] { movie, Album("a", "An Album"), Book("b", "A Book") }, _ => false);

        Assert.Equal(new HashSet<string> { "a", "b" }, works.Select(w => w.GapId).ToHashSet());
    }

    [Fact]
    public void Build_OfNothing_IsEmpty()
        => Assert.Empty(WorksMissingBuilder.Build(Array.Empty<GapItem>(), _ => false));

    [Fact]
    public void Build_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => WorksMissingBuilder.Build(null!, _ => false));
        Assert.Throws<ArgumentNullException>(() => WorksMissingBuilder.Build(Array.Empty<GapItem>(), null!));
        Assert.Throws<ArgumentNullException>(() => WorksMissingBuilder.ToWork(null!));
    }

    [Fact]
    public void OwnershipCacheKey_IsTheSameForTheSameKindsInAnyOrder_AndDiffersForOtherKinds()
    {
        var one = OwnershipCache.KeyFor(new[] { BaseItemKind.MusicAlbum, BaseItemKind.Book });
        var reordered = OwnershipCache.KeyFor(new[] { BaseItemKind.Book, BaseItemKind.MusicAlbum, BaseItemKind.Book });

        Assert.Equal(one, reordered);
        Assert.NotEqual(one, OwnershipCache.KeyFor(new[] { BaseItemKind.MusicAlbum }));
        Assert.NotEqual(OwnershipCache.Key, OwnershipCache.KeyFor(new[] { BaseItemKind.Movie, BaseItemKind.Series }));
    }
}
