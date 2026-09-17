using System.Threading;
using System.Threading.Tasks;
using TMDbLib.Client;
using TMDbLib.Objects.Account;
using TMDbLib.Objects.Authentication;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Search;

namespace Jellyfin.Plugin.MindTheGaps.Services.Tmdb;

internal sealed class TmdbAccountApiFactory : ITmdbAccountApiFactory
{
    public ITmdbAccountApi Create(string apiKey)
        => new TmdbAccountApi(new TMDbClient(apiKey) { ThrowApiExceptions = false, MaxRetryCount = 3 });

    private sealed class TmdbAccountApi : ITmdbAccountApi
    {
        private readonly TMDbClient _client;

        public TmdbAccountApi(TMDbClient client) => _client = client;

        public Task SetSessionInformationAsync(string sessionId, SessionType sessionType)
            => _client.SetSessionInformationAsync(sessionId, sessionType);

        public Task<Token?> AuthenticationRequestAutenticationTokenAsync(CancellationToken cancellationToken)
            => _client.AuthenticationRequestAutenticationTokenAsync(cancellationToken);

        public Task<UserSession?> AuthenticationGetUserSessionAsync(string requestToken, CancellationToken cancellationToken)
            => _client.AuthenticationGetUserSessionAsync(requestToken, cancellationToken);

        public Task<AccountDetails?> AccountGetDetailsAsync(CancellationToken cancellationToken)
            => _client.AccountGetDetailsAsync(cancellationToken);

        public Task<SearchContainer<SearchMovie>?> AccountGetMovieWatchlistAsync(int page, CancellationToken cancellationToken)
            => _client.AccountGetMovieWatchlistAsync(page, cancellationToken: cancellationToken);

        public Task<SearchContainer<SearchTv>?> AccountGetTvWatchlistAsync(int page, CancellationToken cancellationToken)
            => _client.AccountGetTvWatchlistAsync(page, cancellationToken: cancellationToken);

        public Task<SearchContainer<SearchMovie>?> AccountGetFavoriteMoviesAsync(int page, CancellationToken cancellationToken)
            => _client.AccountGetFavoriteMoviesAsync(page, cancellationToken: cancellationToken);

        public Task<SearchContainer<SearchTv>?> AccountGetFavoriteTvAsync(int page, CancellationToken cancellationToken)
            => _client.AccountGetFavoriteTvAsync(page, cancellationToken: cancellationToken);

        public void Dispose() => _client.Dispose();
    }
}
