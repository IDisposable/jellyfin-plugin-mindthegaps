using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Todo;
using Jellyfin.Plugin.MindTheGaps.Model;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class EveryoneWatchlistMapperTests
{
    [Fact]
    public void Build_EmitsAGapForAnOpenRow()
    {
        var row = Row("both", "Mad Max 2", "Movie", providerIds: new Dictionary<string, string> { ["Tmdb"] = "76341" }, requestedBy: ["Ann", "Vic"]);

        var gap = Assert.Single(Build([row], Owns()));

        Assert.Equal("everyonewatchlist:both", gap.Id);
        Assert.Equal(GapPattern.Recommendation, gap.Pattern);
        Assert.Equal(MediaDomain.Movies, gap.Domain);
        Assert.Equal(BaseItemKind.Movie, gap.TargetKind);
        Assert.Equal("Mad Max 2", gap.Name);
        Assert.Equal("76341", gap.ProviderIds["Tmdb"]);
        Assert.Equal(SourceItemTypes.EveryoneWatchlist, gap.SourceItemType);
        Assert.Equal("everyonewatchlist", gap.SourceItemId);
        Assert.Equal("Everyone's watchlist", gap.SourceItemName);
        Assert.Equal("Wanted by Ann, Vic", gap.Overview);
    }

    [Fact]
    public void Build_SkipsARowNobodyIsStillWaitingOn()
    {
        var row = Row("done", "Fetched Already", "Movie", openCount: 0);

        Assert.Empty(Build([row], Owns()));
    }

    [Fact]
    public void Build_SkipsATitleTheLibraryAlreadyOwns()
    {
        var row = Row("both", "Mad Max 2", "Movie", providerIds: new Dictionary<string, string> { ["Tmdb"] = "76341" });

        Assert.Empty(Build([row], Owns(BaseItemKind.Movie, "Tmdb", "76341")));
    }

    [Fact]
    public void Build_SkipsAnEpisodeLevelEntry()
    {
        // An individual missing episode is set-completion content, not a discovery pick; Shows already
        // surfaces it under Set completion.
        var row = Row("ep", "Some Episode", "Episode");

        Assert.Empty(Build([row], Owns()));
    }

    [Fact]
    public void Build_SkipsAnUnparseableKind()
    {
        var row = Row("x", "Mystery", "NotAKind");

        Assert.Empty(Build([row], Owns()));
    }

    [Theory]
    [InlineData("Series", BaseItemKind.Series, MediaDomain.Shows)]
    [InlineData("MusicAlbum", BaseItemKind.MusicAlbum, MediaDomain.Music)]
    [InlineData("Book", BaseItemKind.Book, MediaDomain.Books)]
    public void Build_MapsEachKindToItsDomain(string kindName, BaseItemKind kind, MediaDomain domain)
    {
        var row = Row("x", "Title", kindName, providerIds: new Dictionary<string, string> { ["Tmdb"] = "1" });

        var gap = Assert.Single(Build([row], Owns()));

        Assert.Equal(kind, gap.TargetKind);
        Assert.Equal(domain, gap.Domain);
    }

    [Fact]
    public void Build_MatchesAnOwnedAlbumByArtistAndTitleWhenNoIdOverlaps()
    {
        // Discogs and MusicBrainz share no provider id, so an album's own name-key match (the same
        // fallback LibraryVerifier and GapTargetKey use) is what has to catch an already-owned one.
        var row = Row("album", "Kind of Blue", "MusicAlbum", creator: "Miles Davis");

        var ownership = new OwnershipIndex(new HashSet<string>(StringComparer.Ordinal)
        {
            OwnershipIndex.MakeKey(BaseItemKind.MusicAlbum, OwnershipIndex.NameKeyProvider, OwnershipIndex.NameKey("Miles Davis", "Kind of Blue"))
        });

        Assert.Empty(Build([row], ownership));
    }

    [Fact]
    public void Build_EmitsOneGapPerRowInOrder()
    {
        var rows = new[]
        {
            Row("a", "First", "Movie", providerIds: new Dictionary<string, string> { ["Tmdb"] = "1" }),
            Row("b", "Second", "Movie", providerIds: new Dictionary<string, string> { ["Tmdb"] = "2" })
        };

        var gaps = Build(rows, Owns());

        Assert.Equal(["First", "Second"], gaps.Select(g => g.Name));
    }

    private static List<GapItem> Build(IReadOnlyList<TodoDemandRow> rows, OwnershipIndex ownership)
        => EveryoneWatchlistMapper.Build(rows, ownership).ToList();

    private static TodoDemandRow Row(
        string id,
        string name,
        string targetKindName,
        int openCount = 1,
        IReadOnlyDictionary<string, string>? providerIds = null,
        string[]? requestedBy = null,
        string? creator = null)
        => new()
        {
            Id = id,
            Name = name,
            TargetKindName = targetKindName,
            DomainName = "Movies",
            OpenCount = openCount,
            RequestCount = Math.Max(openCount, 1),
            RequestedBy = requestedBy ?? ["Ann"],
            ProviderIds = providerIds ?? new Dictionary<string, string>(),
            Creator = creator
        };

    private static OwnershipIndex Owns(BaseItemKind kind, string provider, string id)
    {
        var set = new HashSet<string>(StringComparer.Ordinal) { OwnershipIndex.MakeKey(kind, provider, id) };
        return new OwnershipIndex(set);
    }

    private static OwnershipIndex Owns() => new(new HashSet<string>(StringComparer.Ordinal));
}
