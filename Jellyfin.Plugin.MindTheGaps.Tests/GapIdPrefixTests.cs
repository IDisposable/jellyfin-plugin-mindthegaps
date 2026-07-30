using Jellyfin.Plugin.MindTheGaps.Gaps;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// A gap id is a persistence contract (ADR-0008): it is the key in gaps.json, resolutions.json, and todos.json,
// and it rides in a shared report link. Renaming a constant here is silent at compile time and would orphan
// every dismissal and todo a user has. These tests pin the wire values so that rename fails loudly instead.
public class GapIdPrefixTests
{
    [Theory]
    [InlineData("collection:")]
    [InlineData("seriescontent:")]
    [InlineData("curated:")]
    [InlineData("filmography:movie:")]
    [InlineData("filmography:series:")]
    [InlineData("recommendation:movie:")]
    [InlineData("recommendation:series:")]
    [InlineData("bibliography:")]
    [InlineData("openlibrarysubject:")]
    [InlineData("discogslabel:")]
    [InlineData("discogsartist:")]
    [InlineData("discography:")]
    [InlineData("artistworks:")]
    [InlineData("mdblist:")]
    [InlineData("traktlist:")]
    [InlineData("imdblist:")]
    [InlineData("justwatch:")]
    [InlineData("imdbperson:")]
    [InlineData("mdblistwatch:")]
    [InlineData("discogswant:")]
    [InlineData("openlibrarywant:")]
    [InlineData("tvdbfavorite:")]
    [InlineData("traktwatch:")]
    public void EveryPrefixIsStillSpelledTheWayASavedReportHoldsIt(string wireValue)
        => Assert.Contains(wireValue, AllGapIdPrefixes());

    [Fact]
    public void GapIdPrefixesAreTheCompleteSet()
    {
        // Guards the list above: a new prefix must be added to the theory, not just the class.
        Assert.Equal(23, AllGapIdPrefixes().Length);
    }

    [Fact]
    public void CuratedSetKeysKeepTheirWireShape()
    {
        Assert.Equal("company:41077", CuratedSetKeys.Company(41077));
        Assert.Equal("keyword:9715", CuratedSetKeys.Keyword(9715));
        Assert.Equal("list:8267559", CuratedSetKeys.List(8267559));

        // The full id a curated gap carries, so the composition is pinned and not only the parts.
        Assert.Equal("curated:company:41077", string.Concat(GapIdPrefixes.Curated, CuratedSetKeys.Company(41077)));
    }

    [Fact]
    public void SourceItemIdsKeepTheirWireShape()
    {
        // Deliberately inconsistent (some hyphenate the words, some do not). A saved report holds these, so
        // they are pinned as they are rather than tidied.
        Assert.Equal("tmdblist-8267559", SourceItemIds.TmdbList(8267559));
        Assert.Equal("mdblist-14", SourceItemIds.MdbList(14));
        Assert.Equal("traktlist-11416887", SourceItemIds.TraktList("11416887"));
        Assert.Equal("trakt-watchlist-lish408", SourceItemIds.TraktWatchlist("lish408"));
        Assert.Equal("imdblist-ls055576446", SourceItemIds.ImdbList("ls055576446"));
        Assert.Equal("imdbperson-nm0000229", SourceItemIds.ImdbPerson("nm0000229"));
        Assert.Equal("justwatch-watchlist", SourceItemIds.JustWatchList("watchlist"));
        Assert.Equal("mdblist-watchlist", SourceItemIds.MdbListWatchlist);
        Assert.Equal("tvdb-favorites", SourceItemIds.TvdbFavorites);
        Assert.Equal("discogs-wantlist-idisposable", SourceItemIds.DiscogsWantlist("idisposable"));
        Assert.Equal("openlibrary-wanttoread-mekBot", SourceItemIds.OpenLibraryWantToRead("mekBot"));
        Assert.Equal("discogs-label-1", SourceItemIds.DiscogsLabel(1));
        Assert.Equal("openlibrary-subject-science_fiction", SourceItemIds.OpenLibrarySubject("science_fiction"));
    }

    [Fact]
    public void EveryPrefixEndsWithItsSeparator()
    {
        // The clear-down machinery matches on the prefix, so one missing colon would let "discography:" also
        // match a hypothetical "discographyX" id.
        Assert.All(AllGapIdPrefixes(), p => Assert.EndsWith(":", p, System.StringComparison.Ordinal));
    }

    private static string[] AllGapIdPrefixes() =>
    [
        GapIdPrefixes.Collection,
        GapIdPrefixes.SeriesContent,
        GapIdPrefixes.Curated,
        GapIdPrefixes.FilmographyMovie,
        GapIdPrefixes.FilmographySeries,
        GapIdPrefixes.RecommendationMovie,
        GapIdPrefixes.RecommendationSeries,
        GapIdPrefixes.Bibliography,
        GapIdPrefixes.OpenLibrarySubject,
        GapIdPrefixes.DiscogsLabel,
        GapIdPrefixes.DiscogsArtist,
        GapIdPrefixes.Discography,
        GapIdPrefixes.ArtistWorks,
        GapIdPrefixes.MdbList,
        GapIdPrefixes.TraktList,
        GapIdPrefixes.ImdbList,
        GapIdPrefixes.JustWatch,
        GapIdPrefixes.ImdbPerson,
        GapIdPrefixes.MdbListWatchlist,
        GapIdPrefixes.DiscogsWantlist,
        GapIdPrefixes.OpenLibraryWantToRead,
        GapIdPrefixes.TvdbFavorite,
        GapIdPrefixes.TraktWatchlist
    ];
}
