using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.VirtualItems;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class MintClassifierTests
{
    private static GapItem Movie(string? tmdb, string? sourceType = null, string? sourceName = null)
    {
        var ids = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(tmdb))
        {
            ids["Tmdb"] = tmdb;
        }

        return new GapItem
        {
            TargetKind = BaseItemKind.Movie,
            Name = "A Movie",
            ProviderIds = ids,
            SourceItemType = sourceType,
            SourceItemName = sourceName
        };
    }

    [Fact]
    public void Classify_CollectionMovie_IntoOwningBoxSet()
    {
        var c = MintClassifier.Classify(Movie("603", "BoxSet"));
        Assert.True(c.IsMaterializable);
        Assert.Equal(MintContainerKind.OwningBoxSet, c.ContainerKind);
        Assert.Equal("603", c.TmdbId);
        Assert.Null(c.PersonName);
    }

    [Fact]
    public void Classify_FilmographyMovie_IntoCatchAll_AttachesPerson()
    {
        var c = MintClassifier.Classify(Movie("105", "Person", "Robert Zemeckis"));
        Assert.True(c.IsMaterializable);
        Assert.Equal(MintContainerKind.CatchAllCollection, c.ContainerKind);
        Assert.Equal("Robert Zemeckis", c.PersonName);
    }

    [Fact]
    public void Classify_RecommendationMovie_IntoCatchAll_NoPerson()
    {
        var c = MintClassifier.Classify(Movie("11", "Series"));
        Assert.True(c.IsMaterializable);
        Assert.Equal(MintContainerKind.CatchAllCollection, c.ContainerKind);
        Assert.Null(c.PersonName);
    }

    [Fact]
    public void Classify_MovieWithoutTmdb_NotMaterializable()
    {
        var gap = new GapItem
        {
            TargetKind = BaseItemKind.Movie,
            Name = "No id",
            ProviderIds = new Dictionary<string, string> { ["Imdb"] = "tt0000000" }
        };
        var c = MintClassifier.Classify(gap);
        Assert.False(c.IsMaterializable);
        Assert.Contains("TMDB", c.Reason, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_Episode_NotMaterializable_NativeInCore()
    {
        var gap = new GapItem
        {
            TargetKind = BaseItemKind.Episode,
            Name = "Show S01E01",
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "999" }
        };
        var c = MintClassifier.Classify(gap);
        Assert.False(c.IsMaterializable);
        Assert.Contains("natively", c.Reason, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_Series_NotMaterializable()
    {
        var gap = new GapItem
        {
            TargetKind = BaseItemKind.Series,
            Name = "A Series",
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1399" }
        };
        Assert.False(MintClassifier.Classify(gap).IsMaterializable);
    }
}
