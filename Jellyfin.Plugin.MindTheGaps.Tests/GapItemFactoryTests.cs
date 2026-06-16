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
    private static GapItem Create(DateTime? releaseDate = null, IEnumerable<ExternalLink>? extraLinks = null)
        => GapItemFactory.Create(
            id: "gap:1",
            pattern: GapPattern.SetCompletion,
            domain: MediaDomain.Video,
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
    public void Create_NullReleaseDate_NoYear_NotUpcoming()
    {
        var gap = Create(releaseDate: null);
        Assert.Null(gap.Year);
        Assert.False(gap.IsUpcoming);
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
