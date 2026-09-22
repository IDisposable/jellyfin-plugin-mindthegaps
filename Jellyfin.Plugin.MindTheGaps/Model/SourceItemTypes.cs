using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The vocabulary of <see cref="GapItem.SourceItemType"/>: what kind of thing surfaced a gap. Unlike
/// <see cref="MediaDomain"/> and <see cref="GapPattern"/> this is a string on the wire, because the values
/// are a mix of Jellyfin item kinds (BoxSet, Series, Person) and the plugin's own set kinds (a TMDB studio,
/// a Discogs label), with no single enum to draw on. Naming them here is what stops a new source inventing
/// a spelling the dashboard does not recognize.
/// </summary>
public static class SourceItemTypes
{
    /// <summary>A Jellyfin BoxSet standing in for a TMDB collection or franchise.</summary>
    public const string BoxSet = "BoxSet";

    /// <summary>An owned series, for its missing episodes.</summary>
    public const string Series = "Series";

    /// <summary>An owned music artist, for their discography or wider catalog.</summary>
    public const string MusicArtist = "MusicArtist";

    /// <summary>A record label, for its releases.</summary>
    public const string MusicLabel = "MusicLabel";

    /// <summary>A TMDB studio, as a curated set.</summary>
    public const string Studio = "Studio";

    /// <summary>A TMDB keyword, as a curated set.</summary>
    public const string Keyword = "Keyword";

    /// <summary>An OpenLibrary subject, as a curated set of books.</summary>
    public const string Subject = "Subject";

    /// <summary>A person (actor, director, writer), for their filmography.</summary>
    public const string Person = "Person";

    /// <summary>An owned book, standing in for its author's bibliography.</summary>
    public const string Book = "Book";

    /// <summary>An owned movie, as the seed of a recommendation.</summary>
    public const string Movie = "Movie";

    /// <summary>A TMDB list. The wire value is the bare "List" this has always emitted.</summary>
    public const string TmdbList = "List";

    /// <summary>An MDBList list.</summary>
    public const string MdbList = "MdbList";

    /// <summary>A Trakt list.</summary>
    public const string TraktList = "TraktList";

    /// <summary>An IMDb watchlist or list.</summary>
    public const string ImdbList = "ImdbList";

    /// <summary>A JustWatch account list (the watchlist or the likes).</summary>
    public const string JustWatchList = "JustWatchList";

    /// <summary>The MDBList account's own watchlist.</summary>
    public const string MdbListWatchlist = "MdbListWatchlist";

    /// <summary>A Discogs wantlist.</summary>
    public const string DiscogsWantlist = "DiscogsWantlist";

    /// <summary>An OpenLibrary reading-log shelf ("Want to Read").</summary>
    public const string OpenLibraryShelf = "OpenLibraryShelf";

    /// <summary>TheTVDB account's favorite series.</summary>
    public const string TvdbFavorites = "TvdbFavorites";

    /// <summary>A Trakt user's watchlist.</summary>
    public const string TraktWatchlist = "TraktWatchlist";

    /// <summary>The connected TheMovieDb account's watchlist or favorites.</summary>
    public const string TmdbAccountList = "TmdbAccountList";

    /// <summary>One of TMDB's official movie discover feeds (top rated, popular, upcoming, now playing).</summary>
    public const string TmdbMovieDiscover = "TmdbMovieDiscover";

    /// <summary>The union of every user's TODO list, folded to one row per title.</summary>
    public const string EveryoneWatchlist = "EveryoneWatchlist";

    /// <summary>
    /// Gets the source types that are deliberately curated lists, as opposed to a per-title recommendation.
    /// A gap surfaced by both files under the list, which is the more meaningful reason to be shown it.
    /// </summary>
    public static IReadOnlyList<string> CuratedListKinds { get; } =
    [
        TmdbList,
        MdbList,
        TraktList,
        ImdbList,
        JustWatchList,
        MdbListWatchlist,
        DiscogsWantlist,
        OpenLibraryShelf,
        TvdbFavorites,
        TraktWatchlist,
        TmdbAccountList
    ];

    /// <summary>
    /// Gets the list kinds whose contents are public by construction, so a title from one may be shown to
    /// users other than the account that follows it. A private TMDB or Trakt list cannot be read with the
    /// plugin's credentials at all, and TMDB's own feeds are public. Deliberately absent: an MDBList list
    /// (the account's own private lists are readable with its key), an IMDb list (the same source reads the
    /// personal watchlist), and every watchlist, wantlist, favorites and account list. A new source is absent
    /// until someone decides it belongs here.
    /// </summary>
    public static IReadOnlyList<string> PublicListKinds { get; } =
    [
        TmdbList,
        TraktList,
        TmdbMovieDiscover
    ];

    /// <summary>
    /// Gets the discovery kinds in the order the dashboard sections them under Discover, most personal first:
    /// the household's own combined want list, then the lists you keep, then the lists you pointed it at,
    /// then what the recommender made of what you own. The two recommendation types share one section, so
    /// the order is by section and a kind may repeat a section's wording. Same contract as
    /// <see cref="SetKindsInOrder"/>: order is a presentation decision that belongs with the vocabulary
    /// rather than in the dashboard.
    /// </summary>
    public static IReadOnlyList<string> DiscoverKindsInOrder { get; } =
    [
        EveryoneWatchlist,
        TmdbAccountList,
        TraktWatchlist,
        MdbListWatchlist,
        JustWatchList,
        ImdbList,
        DiscogsWantlist,
        OpenLibraryShelf,
        TvdbFavorites,
        TmdbList,
        MdbList,
        TraktList,
        TmdbMovieDiscover,
        Movie,
        Series
    ];

    /// <summary>
    /// Gets the set kinds in the order the dashboard groups them under Set completion, most concrete first:
    /// a franchise or series you are visibly part-way through, then a discography, then the broader curated
    /// sets. The order is a presentation decision, but it belongs with the vocabulary rather than duplicated
    /// in the dashboard, which is where it drifted from before.
    /// </summary>
    public static IReadOnlyList<string> SetKindsInOrder { get; } =
    [
        BoxSet,
        Series,
        MusicArtist,
        MusicLabel,
        Studio,
        Keyword,
        Subject
    ];
}
