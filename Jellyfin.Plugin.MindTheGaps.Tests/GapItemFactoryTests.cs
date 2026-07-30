using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class GapItemFactoryTests
{
    private static GapItem Create(
        DateTime? releaseDate = null,
        IEnumerable<ExternalLink>? extraLinks = null,
        MediaDomain domain = MediaDomain.Movies)
        => GapItemFactory.Create(
            id: "gap:1",
            pattern: GapPattern.SetCompletion,
            domain: domain,
            targetKind: BaseItemKind.Movie,
            name: "The Matrix",
            providerIds: new Dictionary<string, string> { ["Tmdb"] = "603" },
            sourceItemId: "abc",
            sourceItemName: "The Matrix Collection",
            sourceItemType: "BoxSet",
            releaseDate: releaseDate,
            imageUrl: "poster.jpg",
            overview: "overview",
            extraLinks: extraLinks);

    [Fact]
    public void Create_SetsCoreFields()
    {
        var gap = Create();
        Assert.Equal("gap:1", gap.Id);
        Assert.Equal(BaseItemKind.Movie, gap.TargetKind);
        Assert.Equal("Movie", gap.TargetKindName);
        Assert.Equal("The Matrix", gap.Name);
        Assert.Equal("abc", gap.SourceItemId);
        Assert.Equal("The Matrix Collection", gap.SourceItemName);
        Assert.Equal("BoxSet", gap.SourceItemType);
    }

    [Fact]
    public void Create_DerivesYearFromReleaseDate()
    {
        var gap = Create(new DateTime(1999, 3, 31, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1999, gap.Year);
    }

    [Fact]
    public void Create_NullReleaseDate_NoYear()
    {
        var gap = Create(releaseDate: null);
        Assert.Null(gap.Year);
    }

    [Fact]
    public void Create_FutureReleaseDate_IsUpcoming()
    {
        var gap = Create(DateTime.UtcNow.AddYears(1));
        Assert.True(gap.IsUpcoming);
    }

    [Fact]
    public void Create_PastReleaseDate_NotUpcoming()
    {
        var gap = Create(DateTime.UtcNow.AddYears(-1));
        Assert.False(gap.IsUpcoming);
    }

    // An undated movie or series is announced but unscheduled ("Bond 26" in the James Bond
    // collection), since TMDB and the episode providers date anything actually released.
    [Theory]
    [InlineData(MediaDomain.Movies)]
    [InlineData(MediaDomain.Shows)]
    public void Create_NullReleaseDate_ScreenDomain_IsUpcoming(MediaDomain domain)
    {
        var gap = Create(releaseDate: null, domain: domain);
        Assert.True(gap.IsUpcoming);
    }

    // Music and books are the opposite: a sparse Discogs/MusicBrainz/OpenLibrary entry for a
    // long-released title carries no date, so it stays a reported gap rather than being hidden.
    [Theory]
    [InlineData(MediaDomain.Music)]
    [InlineData(MediaDomain.Books)]
    public void Create_NullReleaseDate_NonScreenDomain_NotUpcoming(MediaDomain domain)
    {
        var gap = Create(releaseDate: null, domain: domain);
        Assert.False(gap.IsUpcoming);
    }

    // The accumulate passes carry prior GapItem objects forward untouched, so a scan re-derives this for
    // the whole report. Without that, a title stays "upcoming" forever once its date is in the future.
    [Fact]
    public void RefreshUpcoming_ClearsATitleWhoseReleaseDateHasPassed()
    {
        var stale = Create(DateTime.UtcNow.AddYears(-1));
        stale.IsUpcoming = true;   // what the scan that first found it decided, back when the date was ahead

        GapItemFactory.RefreshUpcoming(new[] { stale });

        Assert.False(stale.IsUpcoming);
    }

    [Fact]
    public void RefreshUpcoming_SetsAnUndatedScreenTitleCarriedFromBefore()
    {
        // A gap saved by an older build, before an absent date counted as announced-but-unscheduled.
        var carried = Create(releaseDate: null, domain: MediaDomain.Movies);
        carried.IsUpcoming = false;

        GapItemFactory.RefreshUpcoming(new[] { carried });

        Assert.True(carried.IsUpcoming);
    }

    [Fact]
    public void RefreshUpcoming_LeavesUndatedMusicAndBooksAlone()
    {
        var album = Create(releaseDate: null, domain: MediaDomain.Music);
        var book = Create(releaseDate: null, domain: MediaDomain.Books);

        GapItemFactory.RefreshUpcoming(new[] { album, book });

        Assert.False(album.IsUpcoming);
        Assert.False(book.IsUpcoming);
    }

    [Fact]
    public void RefreshUpcoming_KeepsAKnownFutureDateUpcoming()
    {
        var future = Create(DateTime.UtcNow.AddYears(1));
        future.IsUpcoming = false;

        GapItemFactory.RefreshUpcoming(new[] { future });

        Assert.True(future.IsUpcoming);
    }

    [Fact]
    public void Create_BuildsLinksFromProviderIds()
    {
        var gap = Create();
        var link = Assert.Single(gap.Links);
        Assert.Equal("TMDB", link.Name);
    }

    [Fact]
    public void Create_AppendsExtraLinks()
    {
        var gap = Create(extraLinks: new[] { new ExternalLink("Trakt", "https://trakt.tv/movies/the-matrix") });
        Assert.Equal(2, gap.Links.Count);
        Assert.Contains(gap.Links, l => l.Name == "Trakt");
    }
}
