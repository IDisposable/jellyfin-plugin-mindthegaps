using System.Globalization;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Builds the synthetic <see cref="Model.GapItem.SourceItemId"/> a source uses when the thing that surfaced a
/// gap is not a library item. An owned BoxSet, series, artist, or person supplies its own Jellyfin guid; a
/// list, label, or subject has none, so one is minted here.
/// </summary>
/// <remarks>
/// These are grouping and clear-down keys, not gap ids, and they persist in a saved report, so the spellings
/// are frozen. They are not consistent with each other (some separate with a hyphen, some do not); naming them
/// in one place makes that visible without changing what a saved report already holds. Pinned by
/// <c>GapIdPrefixTests</c>.
/// </remarks>
internal static class SourceItemIds
{
    /// <summary>
    /// Gets the owner id for the MDBList account's own watchlist. There is only one, so it takes no argument.
    /// </summary>
    public static string MdbListWatchlist => "mdblist-watchlist";

    /// <summary>
    /// Gets the owner id for TheTVDB account's favourites. There is only one set, so it takes no argument.
    /// </summary>
    public static string TvdbFavorites => "tvdb-favorites";

    /// <summary>
    /// Builds the owner id for a TMDB list.
    /// </summary>
    /// <param name="listId">The TMDB list id.</param>
    /// <returns>The owner id.</returns>
    public static string TmdbList(int listId)
        => string.Create(CultureInfo.InvariantCulture, $"tmdblist-{listId}");

    /// <summary>
    /// Builds the owner id for an MDBList list.
    /// </summary>
    /// <param name="listId">The MDBList list id.</param>
    /// <returns>The owner id.</returns>
    public static string MdbList(int listId)
        => string.Create(CultureInfo.InvariantCulture, $"mdblist-{listId}");

    /// <summary>
    /// Builds the owner id for a Trakt list.
    /// </summary>
    /// <param name="listId">The Trakt list id or slug.</param>
    /// <returns>The owner id.</returns>
    public static string TraktList(string listId)
        => string.Create(CultureInfo.InvariantCulture, $"traktlist-{listId}");

    /// <summary>
    /// Builds the owner id for a Trakt user's watchlist.
    /// </summary>
    /// <param name="username">The Trakt username.</param>
    /// <returns>The owner id.</returns>
    public static string TraktWatchlist(string username)
        => string.Create(CultureInfo.InvariantCulture, $"trakt-watchlist-{username}");

    /// <summary>
    /// Builds the owner id for an IMDb watchlist or list.
    /// </summary>
    /// <param name="listId">The IMDb list's "ls" id.</param>
    /// <returns>The owner id.</returns>
    public static string ImdbList(string listId)
        => string.Create(CultureInfo.InvariantCulture, $"imdblist-{listId}");

    /// <summary>
    /// Builds the owner id for a person named on an IMDb people list. The IMDb name id keys it, not the list,
    /// so the same person named on two lists groups once.
    /// </summary>
    /// <param name="imdbNameId">The IMDb name id ("nm0000229").</param>
    /// <returns>The owner id.</returns>
    public static string ImdbPerson(string imdbNameId)
        => string.Create(CultureInfo.InvariantCulture, $"imdbperson-{imdbNameId}");

    /// <summary>
    /// Builds the owner id for a JustWatch account list.
    /// </summary>
    /// <param name="listType">The list type, lower-cased.</param>
    /// <returns>The owner id.</returns>
    public static string JustWatchList(string listType)
        => string.Create(CultureInfo.InvariantCulture, $"justwatch-{listType}");

    /// <summary>
    /// Builds the owner id for a Discogs wantlist.
    /// </summary>
    /// <param name="username">The Discogs username.</param>
    /// <returns>The owner id.</returns>
    public static string DiscogsWantlist(string username)
        => string.Create(CultureInfo.InvariantCulture, $"discogs-wantlist-{username}");

    /// <summary>
    /// Builds the owner id for an OpenLibrary "Want to Read" shelf.
    /// </summary>
    /// <param name="username">The OpenLibrary username.</param>
    /// <returns>The owner id.</returns>
    public static string OpenLibraryWantToRead(string username)
        => string.Create(CultureInfo.InvariantCulture, $"openlibrary-wanttoread-{username}");

    /// <summary>
    /// Builds the owner id for a Discogs record label.
    /// </summary>
    /// <param name="labelId">The Discogs label id.</param>
    /// <returns>The owner id.</returns>
    public static string DiscogsLabel(long labelId)
        => string.Create(CultureInfo.InvariantCulture, $"discogs-label-{labelId}");

    /// <summary>
    /// Builds the owner id for an OpenLibrary subject.
    /// </summary>
    /// <param name="subject">The OpenLibrary subject slug.</param>
    /// <returns>The owner id.</returns>
    public static string OpenLibrarySubject(string subject)
        => string.Create(CultureInfo.InvariantCulture, $"openlibrary-subject-{subject}");
}
