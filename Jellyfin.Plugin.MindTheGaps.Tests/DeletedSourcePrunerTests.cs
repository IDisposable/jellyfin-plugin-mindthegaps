using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class DeletedSourcePrunerTests
{
    private const string Inception = "0123456789abcdef0123456789abcdef";
    private const string Memento = "11111111111111111111111111111111";
    private const string Tenet = "22222222222222222222222222222222";

    private static readonly HashSet<string> InceptionGone = new(StringComparer.OrdinalIgnoreCase) { Inception };

    private static GapSourceRef Ref(string id, string name) => new() { Id = id, Name = name, Type = "Movie", Year = 2000 };

    private static GapItem Rec(params GapSourceRef[] others) => new()
    {
        Id = "rec:movie:1",
        Name = "Some Movie",
        Pattern = GapPattern.Recommendation,
        SourceItemId = Inception,
        SourceItemName = "Inception",
        SourceItemType = "Movie",
        SourceItemYear = 2010,
        SourceLinks = [new ExternalLink { Name = "TMDB", Url = "https://www.themoviedb.org/movie/27205" }],
        OtherSources = others.Length == 0 ? null : others
    };

    [Fact]
    public void Prune_LeavesAGapNamingNothingDeletedAsTheSameInstance()
    {
        var gap = Rec(Ref(Memento, "Memento"));

        Assert.Same(gap, DeletedSourcePruner.Prune(gap, new HashSet<string> { Tenet }));
    }

    [Fact]
    public void Prune_DropsADeletedSecondarySourceAndKeepsThePrimary()
    {
        var gap = Rec(Ref(Memento, "Memento"), Ref(Tenet, "Tenet"));

        var pruned = DeletedSourcePruner.Prune(gap, new HashSet<string> { Memento });

        Assert.NotNull(pruned);
        Assert.NotSame(gap, pruned);
        Assert.Equal(Inception, pruned!.SourceItemId);
        Assert.Single(pruned.SourceLinks);
        var other = Assert.Single(pruned.OtherSources!);
        Assert.Equal("Tenet", other.Name);

        // The stored gap is not edited in place, so a reader serializing it never sees half the change.
        Assert.Equal(2, gap.OtherSources!.Count);
    }

    [Fact]
    public void Prune_PromotesTheFirstSecondarySourceWhenThePrimaryIsDeleted()
    {
        var gap = Rec(Ref(Memento, "Memento"), Ref(Tenet, "Tenet"));

        var pruned = DeletedSourcePruner.Prune(gap, InceptionGone);

        Assert.NotNull(pruned);
        Assert.Equal(Memento, pruned!.SourceItemId);
        Assert.Equal("Memento", pruned.SourceItemName);
        Assert.Equal("Movie", pruned.SourceItemType);
        Assert.Equal(2000, pruned.SourceItemYear);
        Assert.Empty(pruned.SourceLinks);
        Assert.Equal("Tenet", Assert.Single(pruned.OtherSources!).Name);
        Assert.Equal("Inception", gap.SourceItemName);
    }

    [Fact]
    public void Prune_BuildsThePromotedSourcesLinksFromItsProviderIds()
    {
        var studio = new GapSourceRef
        {
            Id = Memento,
            Name = "A24",
            Type = "Studio",
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "41077" }
        };

        var pruned = DeletedSourcePruner.Prune(Rec(studio), InceptionGone);

        Assert.Equal(studio.ProviderIds, pruned!.SourceProviderIds);
        var link = Assert.Single(pruned.SourceLinks);
        Assert.Equal("https://www.themoviedb.org/company/41077", link.Url);
    }

    [Fact]
    public void Prune_PromotesPastADeletedSecondaryAndClearsTheListWhenItEmpties()
    {
        var gap = Rec(Ref(Memento, "Memento"), Ref(Tenet, "Tenet"));

        var pruned = DeletedSourcePruner.Prune(gap, new HashSet<string> { Inception, Memento });

        Assert.Equal(Tenet, pruned!.SourceItemId);
        Assert.Null(pruned.OtherSources);
    }

    [Fact]
    public void Prune_RemovesAGapWhoseOnlySourcesAreDeleted()
    {
        Assert.Null(DeletedSourcePruner.Prune(Rec(), InceptionGone));
        Assert.Null(DeletedSourcePruner.Prune(Rec(Ref(Memento, "Memento")), new HashSet<string> { Inception, Memento }));
    }

    [Fact]
    public void Prune_KeepsASecondarySourceWithNoId()
    {
        var gap = Rec(new GapSourceRef { Name = "Unknown" });

        var pruned = DeletedSourcePruner.Prune(gap, InceptionGone);

        Assert.Null(pruned!.SourceItemId);
        Assert.Equal("Unknown", pruned.SourceItemName);
    }

    [Fact]
    public void LibrarySourceIds_CollectsOnlyLibraryItemIdsFromBothPlaces()
    {
        var list = new GapItem { Id = "imdblist:tt1", SourceItemId = "imdblist-ls123" };
        var curated = new GapItem { Id = "curated:keyword:1:2", SourceItemId = string.Empty };
        var rec = Rec(Ref(Memento, "Memento"), Ref("tmdblist-28", "Best Picture Winners"));

        var ids = DeletedSourcePruner.LibrarySourceIds([list, curated, rec, Rec()]);

        Assert.Equal(2, ids.Count);
        Assert.Contains(Guid.ParseExact(Inception, "N"), ids);
        Assert.Contains(Guid.ParseExact(Memento, "N"), ids);
    }
}
