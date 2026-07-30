namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// The prefix every source stamps on the <see cref="Model.GapItem.Id"/>s it emits, named once. A gap id is a
/// persistence contract (ADR-0008): it survives in `gaps.json`, in `resolutions.json`, in `todos.json`, and in
/// shared report links, so the literals here are frozen and a rename breaks a user's dismissals.
/// </summary>
/// <remarks>
/// Naming them here is not only tidiness. The prefix is the key the clear-down machinery matches on
/// (<see cref="ISetContentSource.GapIdPrefix"/>, <c>GapStore.ReplaceSourceGaps</c>, and
/// <c>GapEngine.RecheckablePrefixes</c>), and until now a source spelled it once in its mapper and again on
/// itself. The two only had to agree; nothing checked that they did, and a typo in either would have made a
/// re-check silently swap nothing. <c>GapIdPrefixTests</c> pins every value.
/// </remarks>
internal static class GapIdPrefixes
{
    /// <summary>A movie missing from a TMDB collection or BoxSet.</summary>
    public const string Collection = "collection:";

    /// <summary>An episode or season missing from an owned series.</summary>
    public const string SeriesContent = "seriescontent:";

    /// <summary>A movie missing from a curated set. The set's own key follows (see <see cref="CuratedSetKeys"/>).</summary>
    public const string Curated = "curated:";

    /// <summary>An unowned movie from an owned person's filmography.</summary>
    public const string FilmographyMovie = "filmography:movie:";

    /// <summary>An unowned series from an owned person's filmography.</summary>
    public const string FilmographySeries = "filmography:series:";

    /// <summary>A movie recommended from an owned movie.</summary>
    public const string RecommendationMovie = "recommendation:movie:";

    /// <summary>A series recommended from an owned series.</summary>
    public const string RecommendationSeries = "recommendation:series:";

    /// <summary>An unowned work from an owned author's bibliography.</summary>
    public const string Bibliography = "bibliography:";

    /// <summary>An unowned work under a curated OpenLibrary subject.</summary>
    public const string OpenLibrarySubject = "openlibrarysubject:";

    /// <summary>An unowned release on a curated Discogs label.</summary>
    public const string DiscogsLabel = "discogslabel:";

    /// <summary>An unowned release by an owned artist, from Discogs.</summary>
    public const string DiscogsArtist = "discogsartist:";

    /// <summary>An unowned studio album by an owned artist (the MusicBrainz discography pass).</summary>
    public const string Discography = "discography:";

    /// <summary>An unowned release from an owned artist's wider catalog.</summary>
    public const string ArtistWorks = "artistworks:";

    /// <summary>An unowned title on an MDBList community list.</summary>
    public const string MdbList = "mdblist:";

    /// <summary>An unowned title on a Trakt list.</summary>
    public const string TraktList = "traktlist:";

    /// <summary>An unowned title on an IMDb watchlist or list.</summary>
    public const string ImdbList = "imdblist:";

    /// <summary>An unowned title on a JustWatch account list.</summary>
    public const string JustWatch = "justwatch:";

    /// <summary>An unowned credit of a person named on an IMDb people list.</summary>
    public const string ImdbPerson = "imdbperson:";

    /// <summary>An unowned title on the MDBList account's own watchlist.</summary>
    public const string MdbListWatchlist = "mdblistwatch:";

    /// <summary>An unowned release on a Discogs wantlist.</summary>
    public const string DiscogsWantlist = "discogswant:";

    /// <summary>An unowned work on an OpenLibrary "Want to Read" shelf.</summary>
    public const string OpenLibraryWantToRead = "openlibrarywant:";

    /// <summary>An unowned series favourited on TheTVDB.</summary>
    public const string TvdbFavorite = "tvdbfavorite:";

    /// <summary>An unowned title on a Trakt user's watchlist.</summary>
    public const string TraktWatchlist = "traktwatch:";
}
