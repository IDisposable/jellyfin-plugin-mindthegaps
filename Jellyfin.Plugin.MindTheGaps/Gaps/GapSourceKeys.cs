namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Every source's naming in one table: the gap-id prefix and, where there is no library item to own the gap,
/// the synthetic owner id. Both come from one stem per source (see <see cref="GapSourceKey"/>), so the two
/// cannot drift apart, and the handful that already shipped mismatched are the only entries that say
/// <c>LegacyOwner</c>.
/// </summary>
/// <remarks>
/// These strings are a persistence contract (ADR-0008): they key `gaps.json`, `resolutions.json`, and
/// `todos.json`, and they ride in shared report links. The prefix is also what
/// <see cref="ISetContentSource.GapIdPrefix"/>, <c>GapStore.ReplaceSourceGaps</c>, and
/// <c>GapEngine.RecheckablePrefixes</c> match on. <c>GapIdPrefixTests</c> pins every value.
/// </remarks>
internal static class GapSourceKeys
{
    /// <summary>Gets the key for a movie missing from a TMDB collection or BoxSet; owned by the BoxSet.</summary>
    public static GapSourceKey Collection { get; } = GapSourceKey.GapOnly("collection");

    /// <summary>Gets the key for an episode or season missing from an owned series; owned by the series.</summary>
    public static GapSourceKey SeriesContent { get; } = GapSourceKey.GapOnly("seriescontent");

    /// <summary>Gets the key for a movie missing from a curated set. The set's own key follows (see <see cref="CuratedSetKeys"/>).</summary>
    public static GapSourceKey Curated { get; } = GapSourceKey.GapOnly("curated");

    /// <summary>
    /// Gets the key for a curated TMDB list. Its gaps are keyed under <see cref="Curated"/>, so it
    /// contributes an owner id only, and that owner id shipped as "tmdblist" rather than matching the "list"
    /// set key.
    /// </summary>
    public static GapSourceKey TmdbList { get; } = GapSourceKey.OwnerOnly("tmdblist");

    /// <summary>Gets the key for an unowned movie from an owned person's filmography; owned by the person.</summary>
    public static GapSourceKey FilmographyMovie { get; } = GapSourceKey.GapOnly("filmography:movie");

    /// <summary>Gets the key for an unowned series from an owned person's filmography; owned by the person.</summary>
    public static GapSourceKey FilmographySeries { get; } = GapSourceKey.GapOnly("filmography:series");

    /// <summary>Gets the key for a movie recommended from an owned movie; owned by that movie.</summary>
    public static GapSourceKey RecommendationMovie { get; } = GapSourceKey.GapOnly("recommendation:movie");

    /// <summary>Gets the key for a series recommended from an owned series; owned by that series.</summary>
    public static GapSourceKey RecommendationSeries { get; } = GapSourceKey.GapOnly("recommendation:series");

    /// <summary>Gets the key for an unowned work from an owned author's bibliography; owned by the owned book.</summary>
    public static GapSourceKey Bibliography { get; } = GapSourceKey.GapOnly("bibliography");

    /// <summary>Gets the key for an unowned release by an owned artist, from Discogs; owned by the artist.</summary>
    public static GapSourceKey DiscogsArtist { get; } = GapSourceKey.GapOnly("discogsartist");

    /// <summary>Gets the key for an unowned studio album by an owned artist (MusicBrainz); owned by the artist.</summary>
    public static GapSourceKey Discography { get; } = GapSourceKey.GapOnly("discography");

    /// <summary>Gets the key for an unowned release from an owned artist's wider catalog; owned by the artist.</summary>
    public static GapSourceKey ArtistWorks { get; } = GapSourceKey.GapOnly("artistworks");

    /// <summary>Gets the key for an unowned work under a curated OpenLibrary subject. Shipped with a hyphenated owner stem.</summary>
    public static GapSourceKey OpenLibrarySubject { get; } = GapSourceKey.LegacyOwner("openlibrarysubject", "openlibrary-subject");

    /// <summary>Gets the key for an unowned release on a curated Discogs label. Shipped with a hyphenated owner stem.</summary>
    public static GapSourceKey DiscogsLabel { get; } = GapSourceKey.LegacyOwner("discogslabel", "discogs-label");

    /// <summary>Gets the key for an unowned title on an MDBList community list.</summary>
    public static GapSourceKey MdbList { get; } = GapSourceKey.For("mdblist");

    /// <summary>Gets the key for an unowned title on a Trakt list.</summary>
    public static GapSourceKey TraktList { get; } = GapSourceKey.For("traktlist");

    /// <summary>Gets the key for an unowned title on an IMDb watchlist or list.</summary>
    public static GapSourceKey ImdbList { get; } = GapSourceKey.For("imdblist");

    /// <summary>Gets the key for an unowned credit of a person named on an IMDb people list.</summary>
    public static GapSourceKey ImdbPerson { get; } = GapSourceKey.For("imdbperson");

    /// <summary>Gets the key for an unowned title on a JustWatch account list.</summary>
    public static GapSourceKey JustWatch { get; } = GapSourceKey.For("justwatch");

    /// <summary>Gets the key for an unowned title on the MDBList account's own watchlist.</summary>
    public static GapSourceKey MdbListWatchlist { get; } = GapSourceKey.For("mdblistwatchlist");

    /// <summary>Gets the key for an unowned release on a Discogs wantlist.</summary>
    public static GapSourceKey DiscogsWantlist { get; } = GapSourceKey.For("discogswantlist");

    /// <summary>Gets the key for an unowned work on an OpenLibrary "Want to Read" shelf.</summary>
    public static GapSourceKey OpenLibraryWantToRead { get; } = GapSourceKey.For("openlibrarywanttoread");

    /// <summary>Gets the key for an unowned series favorited on TheTVDB.</summary>
    public static GapSourceKey TvdbFavorites { get; } = GapSourceKey.For("tvdbfavorites");

    /// <summary>Gets the key for an unowned title on a Trakt user's watchlist.</summary>
    public static GapSourceKey TraktWatchlist { get; } = GapSourceKey.For("traktwatchlist");

    /// <summary>Gets the key for an unowned title on the connected TheMovieDb account's watchlist or favorites.</summary>
    public static GapSourceKey TmdbAccountList { get; } = GapSourceKey.For("tmdbaccount");
}
