using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class OwnershipIndexTests
{
    // Owns/OwnsAny only test key membership, so null item values are sufficient here.
    private static OwnershipIndex IndexWith(params string[] keys)
    {
        var dict = new Dictionary<string, BaseItem>();
        foreach (var key in keys)
        {
            dict[key] = null!;
        }

        return new OwnershipIndex(dict);
    }

    [Fact]
    public void MakeKey_NormalizesToLowercase()
    {
        Assert.Equal("movie|tmdb|603", OwnershipIndex.MakeKey(BaseItemKind.Movie, "Tmdb", "603"));
    }

    [Fact]
    public void Owns_IsCaseInsensitive()
    {
        var index = IndexWith(OwnershipIndex.MakeKey(BaseItemKind.Movie, "Tmdb", "603"));
        Assert.True(index.Owns(BaseItemKind.Movie, "TMDB", "603"));
    }

    [Fact]
    public void Owns_DistinguishesKind()
    {
        var index = IndexWith(OwnershipIndex.MakeKey(BaseItemKind.Movie, "Tmdb", "603"));
        Assert.False(index.Owns(BaseItemKind.Series, "Tmdb", "603"));
    }

    [Fact]
    public void OwnsAny_MatchesOnAnyProviderId()
    {
        var index = IndexWith(OwnershipIndex.MakeKey(BaseItemKind.Movie, "Imdb", "tt0133093"));
        var candidate = new Dictionary<string, string> { ["Tmdb"] = "603", ["Imdb"] = "tt0133093" };
        Assert.True(index.OwnsAny(BaseItemKind.Movie, candidate));
    }

    [Fact]
    public void OwnsAny_ReturnsFalseWhenNoIdMatches()
    {
        var index = IndexWith(OwnershipIndex.MakeKey(BaseItemKind.Movie, "Tmdb", "999"));
        var candidate = new Dictionary<string, string> { ["Tmdb"] = "603", ["Imdb"] = "tt0133093" };
        Assert.False(index.OwnsAny(BaseItemKind.Movie, candidate));
    }

    [Fact]
    public void OwnsAny_IgnoresEmptyValues()
    {
        var index = IndexWith(OwnershipIndex.MakeKey(BaseItemKind.Movie, "Tmdb", "603"));
        var candidate = new Dictionary<string, string> { ["Tmdb"] = string.Empty };
        Assert.False(index.OwnsAny(BaseItemKind.Movie, candidate));
    }
}
