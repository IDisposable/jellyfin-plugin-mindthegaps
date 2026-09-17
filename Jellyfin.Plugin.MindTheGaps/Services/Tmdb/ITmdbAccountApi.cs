using System;
using System.Threading;
using System.Threading.Tasks;
using TMDbLib.Objects.Account;
using TMDbLib.Objects.Authentication;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Search;

namespace Jellyfin.Plugin.MindTheGaps.Services.Tmdb;

internal interface ITmdbAccountApi : IDisposable
{
    Task SetSessionInformationAsync(string sessionId, SessionType sessionType);

    Task<Token?> AuthenticationRequestAutenticationTokenAsync(CancellationToken cancellationToken);

    Task<UserSession?> AuthenticationGetUserSessionAsync(string requestToken, CancellationToken cancellationToken);

    Task<AccountDetails?> AccountGetDetailsAsync(CancellationToken cancellationToken);

    Task<SearchContainer<SearchMovie>?> AccountGetMovieWatchlistAsync(int page, CancellationToken cancellationToken);

    Task<SearchContainer<SearchTv>?> AccountGetTvWatchlistAsync(int page, CancellationToken cancellationToken);

    Task<SearchContainer<SearchMovie>?> AccountGetFavoriteMoviesAsync(int page, CancellationToken cancellationToken);

    Task<SearchContainer<SearchTv>?> AccountGetFavoriteTvAsync(int page, CancellationToken cancellationToken);
}
