using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class GapTargetKeyTests
{
    private static GapItem Movie(string id, string tmdb, string? imdb = null)
    {
        var ids = new Dictionary<string, string> { ["Tmdb"] = tmdb };
        if (imdb is not null)
        {
            ids["Imdb"] = imdb;
        }

        return new GapItem { Id = id, TargetKind = BaseItemKind.Movie, Name = "Mad Max 2", ProviderIds = ids };
    }

    [Fact]
    public void MatchingIds_FindsEveryGapAboutTheSameTitle()
    {
        // One acquired film is a hole in its collection, a studio set, a filmography, a list, and a
        // recommendation, each with its own id. Confirming it owned must clear all of them.
        var report = new[]
        {
            Movie("collection:8945:76341", "76341"),
            Movie("curated:studio-123:76341", "76341"),
            Movie("filmography:movie:76341", "76341"),
            Movie("recommendation:movie:76341", "76341"),
            Movie("mdblist:99:76341", "76341"),
            Movie("collection:8945:76342", "76342")   // a different film in the same collection
        };

        var matched = GapTargetKey.MatchingIds(report, new[] { report[0] });

        Assert.Equal(5, matched.Count);
        Assert.DoesNotContain("collection:8945:76342", matched);
    }

    [Fact]
    public void MatchingIds_MatchesOnAnySharedProviderId()
    {
        // The background availability pass resolves extra ids onto some rows but not others, so two gaps
        // about one film can overlap on only one provider.
        var enriched = Movie("collection:8945:76341", "76341", "tt0082694");
        var tmdbOnly = Movie("recommendation:movie:76341", "76341");
        var imdbOnly = new GapItem
        {
            Id = "mdblist:99:tt0082694",
            TargetKind = BaseItemKind.Movie,
            Name = "Mad Max 2",
            ProviderIds = new Dictionary<string, string> { ["Imdb"] = "tt0082694" }
        };

        var matched = GapTargetKey.MatchingIds(new[] { enriched, tmdbOnly, imdbOnly }, new[] { enriched });

        Assert.Equal(3, matched.Count);
    }

    [Fact]
    public void MatchingIds_FollowsProviderIdLinksTransitively()
    {
        // The three representations one film gets as the availability pass resolves ids onto some rows and
        // not others. Clicking the TMDB-only row shares no key with the IMDb-only row, so they are linked
        // only through the enriched row in the middle.
        var tmdbOnly = Movie("collection:8945:76341", "76341");
        var both = Movie("curated:studio-1:76341", "76341", "tt0082694");
        var imdbOnly = new GapItem
        {
            Id = "mdblist:99:tt0082694",
            TargetKind = BaseItemKind.Movie,
            Name = "Mad Max 2",
            ProviderIds = new Dictionary<string, string> { ["Imdb"] = "tt0082694" }
        };
        var unrelated = Movie("collection:8945:76342", "76342");

        var matched = GapTargetKey.MatchingIds(new[] { tmdbOnly, both, imdbOnly, unrelated }, new[] { tmdbOnly });

        Assert.Equal(3, matched.Count);
        Assert.Contains("mdblist:99:tt0082694", matched, StringComparer.Ordinal);
        Assert.DoesNotContain("collection:8945:76342", matched, StringComparer.Ordinal);
    }

    [Fact]
    public void MatchingIds_ClearingAnyRepresentation_ClearsThemAll()
    {
        // The closure must not depend on which row was clicked.
        var tmdbOnly = Movie("a", "76341");
        var both = Movie("b", "76341", "tt0082694");
        var imdbOnly = new GapItem
        {
            Id = "c",
            TargetKind = BaseItemKind.Movie,
            Name = "Mad Max 2",
            ProviderIds = new Dictionary<string, string> { ["Imdb"] = "tt0082694" }
        };
        var all = new[] { tmdbOnly, both, imdbOnly };

        foreach (var clicked in all)
        {
            Assert.Equal(3, GapTargetKey.MatchingIds(all, new[] { clicked }).Count);
        }
    }

    [Fact]
    public void MatchingIds_DoesNotChainAcrossItemKinds()
    {
        // Transitivity must not leak between kinds: a series sharing a TMDB id with a movie is not a bridge
        // to that series' other ids.
        var movie = Movie("m", "1");
        var series = new GapItem
        {
            Id = "s",
            TargetKind = BaseItemKind.Series,
            Name = "Same Number",
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1", ["Imdb"] = "tt999" }
        };
        var otherSeries = new GapItem
        {
            Id = "s2",
            TargetKind = BaseItemKind.Series,
            Name = "Linked By Imdb",
            ProviderIds = new Dictionary<string, string> { ["Imdb"] = "tt999" }
        };

        var matched = GapTargetKey.MatchingIds(new[] { movie, series, otherSeries }, new[] { movie });

        Assert.Equal("m", Assert.Single(matched));
    }

    [Fact]
    public void MatchingIds_DoesNotCrossItemKinds()
    {
        // A series and a movie can share a TMDB id; they are not the same thing.
        var movie = Movie("filmography:movie:1", "1");
        var series = new GapItem
        {
            Id = "filmography:series:1",
            TargetKind = BaseItemKind.Series,
            Name = "Same Number",
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1" }
        };

        var matched = GapTargetKey.MatchingIds(new[] { movie, series }, new[] { movie });

        Assert.Equal("filmography:movie:1", Assert.Single(matched));
    }

    [Fact]
    public void MatchingIds_MatchesAlbumsByArtistAndTitleWhenIdsDoNotOverlap()
    {
        // A Discogs release and a MusicBrainz release group for one record share no provider id, so the
        // name key is the only thing that can link them (the same fallback LibraryVerifier uses).
        var musicBrainz = new GapItem
        {
            Id = "discography:mb-artist:rg-1",
            TargetKind = BaseItemKind.MusicAlbum,
            Name = "Kind of Blue",
            SourceItemName = "Miles Davis",
            ProviderIds = new Dictionary<string, string> { ["MusicBrainzReleaseGroup"] = "rg-1" }
        };
        var discogs = new GapItem
        {
            Id = "discogsartist:55:r-9",
            TargetKind = BaseItemKind.MusicAlbum,
            Name = "Kind of Blue",
            SourceItemName = "Miles Davis",
            ProviderIds = new Dictionary<string, string> { ["Discogs"] = "r-9" }
        };

        var matched = GapTargetKey.MatchingIds(new[] { musicBrainz, discogs }, new[] { musicBrainz });

        Assert.Equal(2, matched.Count);
    }

    [Fact]
    public void MatchingIds_WithNothingToMatchOn_ReturnsEmpty()
    {
        var idless = new GapItem { Id = "x", TargetKind = BaseItemKind.Movie, Name = "No ids" };

        Assert.Empty(GapTargetKey.MatchingIds(new[] { idless }, new[] { idless }));
        Assert.Empty(GapTargetKey.MatchingIds(new[] { Movie("a", "1") }, Enumerable.Empty<GapItem>()));
    }
}
