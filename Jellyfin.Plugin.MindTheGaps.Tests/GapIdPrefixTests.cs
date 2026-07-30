using Jellyfin.Plugin.MindTheGaps.Gaps;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// A gap id is a persistence contract (ADR-0008): it is the key in gaps.json, resolutions.json, and todos.json,
// and it rides in a shared report link. Renaming a stem in GapSourceKeys is silent at compile time and would
// orphan every dismissal and todo a user has. These tests pin the wire values so that rename fails loudly.
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
    [InlineData("mdblistwatchlist:")]
    [InlineData("discogswantlist:")]
    [InlineData("openlibrarywanttoread:")]
    [InlineData("tvdbfavorites:")]
    [InlineData("traktwatchlist:")]
    [InlineData("tmdbaccount:")]
    public void EveryPrefixIsStillSpelledTheWayASavedReportHoldsIt(string wireValue)
        => Assert.Contains(wireValue, AllGapIdPrefixes());

    [Fact]
    public void GapIdPrefixesAreTheCompleteSet()
    {
        // Guards the list above: a new prefix must be added to the theory, not just the table.
        Assert.Equal(24, AllGapIdPrefixes().Length);
    }

    [Fact]
    public void AnOwnerIdIsTheGapPrefixWithAHyphen()
    {
        // The point of one stem per source: the two halves cannot drift. Anything failing here is either a
        // typo or a source that should have been declared with LegacyOwner.
        Assert.All(Unified(), key => Assert.Equal(
            key.GapPrefix.TrimEnd(':') + "-x",
            key.Owner("x")));
    }

    [Fact]
    public void OnlyTheThreeShippedMismatchesDiverge()
    {
        // These three reached users spelled this way, so they are pinned rather than tidied. Everything else
        // derives mechanically, and a fourth exception should have to be argued for.
        Assert.Equal("openlibrary-subject-science_fiction", GapSourceKeys.OpenLibrarySubject.Owner("science_fiction"));
        Assert.Equal("discogs-label-1", GapSourceKeys.DiscogsLabel.Owner(1));
        Assert.Equal("tmdblist-8267559", GapSourceKeys.TmdbList.Owner(8267559));

        // ...and the first two do not match their own gap prefix, which is exactly what makes them legacy.
        Assert.Equal("openlibrarysubject:", GapSourceKeys.OpenLibrarySubject.GapPrefix);
        Assert.Equal("discogslabel:", GapSourceKeys.DiscogsLabel.GapPrefix);
    }

    [Fact]
    public void CuratedSetKeysKeepTheirWireShape()
    {
        Assert.Equal("company:41077", CuratedSetKeys.Company(41077));
        Assert.Equal("keyword:9715", CuratedSetKeys.Keyword(9715));
        Assert.Equal("list:8267559", CuratedSetKeys.List(8267559));

        // The full id a curated gap carries, so the composition is pinned and not only the parts.
        Assert.Equal("curated:company:41077", GapSourceKeys.Curated.Gap(CuratedSetKeys.Company(41077)));
    }

    [Fact]
    public void OwnerIdsKeepTheirWireShape()
    {
        Assert.Equal("mdblist-14", GapSourceKeys.MdbList.Owner(14));
        Assert.Equal("traktlist-11416887", GapSourceKeys.TraktList.Owner("11416887"));
        Assert.Equal("traktwatchlist-lish408", GapSourceKeys.TraktWatchlist.Owner("lish408"));
        Assert.Equal("imdblist-ls055576446", GapSourceKeys.ImdbList.Owner("ls055576446"));
        Assert.Equal("imdbperson-nm0000229", GapSourceKeys.ImdbPerson.Owner("nm0000229"));
        Assert.Equal("justwatch-watchlist", GapSourceKeys.JustWatch.Owner("watchlist"));
        Assert.Equal("mdblistwatchlist", GapSourceKeys.MdbListWatchlist.Owner());
        Assert.Equal("tvdbfavorites", GapSourceKeys.TvdbFavorites.Owner());
        Assert.Equal("tmdbaccount-watchlist", GapSourceKeys.TmdbAccountList.Owner("watchlist"));
        Assert.Equal("discogswantlist-idisposable", GapSourceKeys.DiscogsWantlist.Owner("idisposable"));
        Assert.Equal("openlibrarywanttoread-mekBot", GapSourceKeys.OpenLibraryWantToRead.Owner("mekBot"));
    }

    [Fact]
    public void ASourceOwnedByALibraryItemHasNoSyntheticOwner()
    {
        // Asking for one is a bug, not a silent empty string: those gaps carry the owning item's guid.
        Assert.Null(GapSourceKeys.Collection.OwnerStem);
        Assert.Throws<System.InvalidOperationException>(() => GapSourceKeys.Collection.Owner());
    }

    [Fact]
    public void EveryPrefixEndsWithItsSeparator()
    {
        // The clear-down machinery matches on the prefix, so one missing colon would let "discography:" also
        // match a hypothetical "discographyX" id.
        Assert.All(AllGapIdPrefixes(), p => Assert.EndsWith(":", p, System.StringComparison.Ordinal));
    }

    // The sources whose two halves are expected to derive from one stem.
    private static GapSourceKey[] Unified() =>
    [
        GapSourceKeys.MdbList,
        GapSourceKeys.TraktList,
        GapSourceKeys.ImdbList,
        GapSourceKeys.ImdbPerson,
        GapSourceKeys.JustWatch,
        GapSourceKeys.MdbListWatchlist,
        GapSourceKeys.DiscogsWantlist,
        GapSourceKeys.OpenLibraryWantToRead,
        GapSourceKeys.TvdbFavorites,
        GapSourceKeys.TraktWatchlist,
        GapSourceKeys.TmdbAccountList
    ];

    private static string[] AllGapIdPrefixes() =>
    [
        GapSourceKeys.Collection.GapPrefix,
        GapSourceKeys.SeriesContent.GapPrefix,
        GapSourceKeys.Curated.GapPrefix,
        GapSourceKeys.FilmographyMovie.GapPrefix,
        GapSourceKeys.FilmographySeries.GapPrefix,
        GapSourceKeys.RecommendationMovie.GapPrefix,
        GapSourceKeys.RecommendationSeries.GapPrefix,
        GapSourceKeys.Bibliography.GapPrefix,
        GapSourceKeys.OpenLibrarySubject.GapPrefix,
        GapSourceKeys.DiscogsLabel.GapPrefix,
        GapSourceKeys.DiscogsArtist.GapPrefix,
        GapSourceKeys.Discography.GapPrefix,
        GapSourceKeys.ArtistWorks.GapPrefix,
        GapSourceKeys.MdbList.GapPrefix,
        GapSourceKeys.TraktList.GapPrefix,
        GapSourceKeys.ImdbList.GapPrefix,
        GapSourceKeys.JustWatch.GapPrefix,
        GapSourceKeys.ImdbPerson.GapPrefix,
        GapSourceKeys.MdbListWatchlist.GapPrefix,
        GapSourceKeys.DiscogsWantlist.GapPrefix,
        GapSourceKeys.OpenLibraryWantToRead.GapPrefix,
        GapSourceKeys.TvdbFavorites.GapPrefix,
        GapSourceKeys.TraktWatchlist.GapPrefix,
        GapSourceKeys.TmdbAccountList.GapPrefix
    ];
}
