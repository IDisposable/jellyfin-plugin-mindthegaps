namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Which of the connected TheMovieDb account's own lists a gap came from. Only the want-lists are here: a
/// rated or watched list is the opposite of a gap.
/// </summary>
internal enum TmdbAccountListKind
{
    /// <summary>The account's watchlist.</summary>
    Watchlist,

    /// <summary>The account's favorites.</summary>
    Favorites
}
