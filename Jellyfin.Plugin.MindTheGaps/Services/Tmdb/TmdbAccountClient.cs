using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TMDbLib.Client;
using TMDbLib.Objects.Authentication;
using TMDbLib.Objects.Search;

namespace Jellyfin.Plugin.MindTheGaps.Services.Tmdb;

/// <summary>
/// Reads the signed-in TheMovieDb account: the session handshake and the account's own watchlist and
/// favorites. Separate from <see cref="TmdbClient"/>, which is the catalog reader, for two reasons that both
/// matter.
/// </summary>
/// <remarks>
/// <para>
/// First, a TMDB session is bound to the <b>application</b> whose api key minted it. The catalog reader falls
/// back to the api key Jellyfin ships (a copy of the one in the server's own
/// <c>MediaBrowser.Providers/Plugins/Tmdb/TmdbUtils.cs</c>, registered to the Jellyfin project and shared by
/// every install). Reading the public catalog through it is what it is published for; minting account
/// sessions through it is not, so everything here refuses to run without the user's own key.
/// </para>
/// <para>
/// Second, <see cref="TmdbClient"/> builds its inner client once and holds it, so a key entered after the
/// server started would not be picked up until a restart. That is harmless for catalog reads and would be a
/// real fault here, because the session would silently be minted against the wrong application. This client
/// therefore rebuilds whenever the key or the session changes.
/// </para>
/// </remarks>
public sealed class TmdbAccountClient : IDisposable
{
    private readonly ILogger<TmdbAccountClient> _logger;
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private TMDbClient? _client;
    private string? _builtForKey;
    private string? _builtForSession;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbAccountClient"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public TmdbAccountClient(ILogger<TmdbAccountClient> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the user's own api key, or null when they have not set one. Never falls back to the shipped
    /// default: see the remarks on the class.
    /// </summary>
    private static string? OwnApiKey
    {
        get
        {
            var key = Plugin.Instance?.Configuration.TmdbApiKey;
            return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        }
    }

    private static string? SessionId
    {
        get
        {
            var id = Plugin.Instance?.Configuration.TmdbSessionId;
            return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }
    }

    /// <summary>
    /// Determines whether the account features can run at all: the user's own api key is set.
    /// </summary>
    /// <returns><see langword="true"/> when an own key is configured.</returns>
    public static bool HasOwnApiKey() => OwnApiKey is not null;

    /// <summary>
    /// Starts the handshake: asks TMDB for a request token the user then approves in their browser. The
    /// token is short-lived (about an hour) and is useless until approved.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request token, or null when there is no own key or TMDB refused.</returns>
    public async Task<string?> CreateRequestTokenAsync(CancellationToken cancellationToken)
    {
        var client = await BuildAsync(withSession: false, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return null;
        }

        var token = await client.AuthenticationRequestAutenticationTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token?.Success != true)
        {
            _logger.LogWarning("TMDB: could not create a request token; check the API key");
            return null;
        }

        return token.RequestToken;
    }

    /// <summary>
    /// Finishes the handshake, exchanging an approved request token for a session id. TMDB session ids do not
    /// expire, so this runs once per account.
    /// </summary>
    /// <param name="requestToken">The token the user approved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The session id, or null when the token was never approved.</returns>
    public async Task<string?> CreateSessionAsync(string requestToken, CancellationToken cancellationToken)
    {
        var client = await BuildAsync(withSession: false, cancellationToken).ConfigureAwait(false);
        if (client is null || string.IsNullOrWhiteSpace(requestToken))
        {
            return null;
        }

        var session = await client.AuthenticationGetUserSessionAsync(requestToken, cancellationToken).ConfigureAwait(false);
        if (session?.Success != true || string.IsNullOrEmpty(session.SessionId))
        {
            // TMDB answers an unapproved token with "Session denied", which is the expected case when the
            // user clicks Finish before approving rather than a fault worth a warning.
            _logger.LogInformation("TMDB: the request token has not been approved yet");
            return null;
        }

        return session.SessionId;
    }

    /// <summary>
    /// Reads the account the stored session belongs to, which is both the "who am I connected as" check for
    /// the settings page and how the account id for the watchlist calls is obtained.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The account's username, or null when not connected.</returns>
    public async Task<string?> GetAccountNameAsync(CancellationToken cancellationToken)
    {
        var client = await BuildAsync(withSession: true, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return null;
        }

        try
        {
            var account = await client.AccountGetDetailsAsync(cancellationToken).ConfigureAwait(false);
            return account?.Username;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "TMDB: could not read the account; the session may have been revoked");
            return null;
        }
    }

    /// <summary>
    /// Reads the account's movie watchlist, following the pages. Returns null when not connected or the read
    /// failed, so a caller can tell an empty watchlist from an unreachable one.
    /// </summary>
    /// <param name="maxItems">The most entries to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The wanted movies, or null.</returns>
    public Task<IReadOnlyList<SearchMovie>?> GetMovieWatchlistAsync(int maxItems, CancellationToken cancellationToken)
        => PageAsync<SearchMovie>(
            (client, page, ct) => client.AccountGetMovieWatchlistAsync(page, cancellationToken: ct),
            "movie watchlist",
            maxItems,
            cancellationToken);

    /// <summary>
    /// Reads the account's series watchlist, following the pages.
    /// </summary>
    /// <param name="maxItems">The most entries to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The wanted series, or null.</returns>
    public Task<IReadOnlyList<SearchTv>?> GetSeriesWatchlistAsync(int maxItems, CancellationToken cancellationToken)
        => PageAsync<SearchTv>(
            (client, page, ct) => client.AccountGetTvWatchlistAsync(page, cancellationToken: ct),
            "series watchlist",
            maxItems,
            cancellationToken);

    /// <summary>
    /// Reads the account's favorite movies, following the pages.
    /// </summary>
    /// <param name="maxItems">The most entries to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The favorite movies, or null.</returns>
    public Task<IReadOnlyList<SearchMovie>?> GetFavoriteMoviesAsync(int maxItems, CancellationToken cancellationToken)
        => PageAsync<SearchMovie>(
            (client, page, ct) => client.AccountGetFavoriteMoviesAsync(page, cancellationToken: ct),
            "favorite movies",
            maxItems,
            cancellationToken);

    /// <summary>
    /// Reads the account's favorite series, following the pages.
    /// </summary>
    /// <param name="maxItems">The most entries to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The favorite series, or null.</returns>
    public Task<IReadOnlyList<SearchTv>?> GetFavoriteSeriesAsync(int maxItems, CancellationToken cancellationToken)
        => PageAsync<SearchTv>(
            (client, page, ct) => client.AccountGetFavoriteTvAsync(page, cancellationToken: ct),
            "favorite series",
            maxItems,
            cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        _client?.Dispose();
        _clientLock.Dispose();
    }

    private async Task<IReadOnlyList<T>?> PageAsync<T>(
        Func<TMDbClient, int, CancellationToken, Task<TMDbLib.Objects.General.SearchContainer<T>?>> fetch,
        string what,
        int maxItems,
        CancellationToken cancellationToken)
    {
        var client = await BuildAsync(withSession: true, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return null;
        }

        var results = new List<T>();
        try
        {
            for (var page = 1; results.Count < maxItems; page++)
            {
                var container = await fetch(client, page, cancellationToken).ConfigureAwait(false);
                if (container?.Results is null)
                {
                    return results.Count > 0 ? results : null;
                }

                results.AddRange(container.Results);
                if (page >= container.TotalPages || container.Results.Count == 0)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "TMDB: failed to read the {What}", what);
            return results.Count > 0 ? results : null;
        }

        return results;
    }

    // Builds (or reuses) a client for the current key and session. Rebuilt whenever either changes, so a key
    // or session edited in the settings page takes effect without a server restart.
    private async Task<TMDbClient?> BuildAsync(bool withSession, CancellationToken cancellationToken)
    {
        var key = OwnApiKey;
        if (key is null)
        {
            _logger.LogWarning(
                "TMDB account: no TMDB API key of your own is set. A session belongs to the application that created it, and the built-in fallback key is Jellyfin's, so account features stay off until you enter your own key");
            return null;
        }

        var session = withSession ? SessionId : null;
        if (withSession && session is null)
        {
            return null;
        }

        await _clientLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null
                && string.Equals(_builtForKey, key, StringComparison.Ordinal)
                && string.Equals(_builtForSession, session, StringComparison.Ordinal))
            {
                return _client;
            }

            _client?.Dispose();
            _client = new TMDbClient(key) { ThrowApiExceptions = false, MaxRetryCount = 3 };
            _builtForKey = key;
            _builtForSession = session;

            if (session is not null)
            {
                await _client.SetSessionInformationAsync(session, SessionType.UserSession).ConfigureAwait(false);
            }

            return _client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>
    /// Builds the page the user approves a request token on. The redirect parameter is deliberately omitted:
    /// without it TMDB needs no callback, which is what lets this work on a server with no public address.
    /// </summary>
    /// <param name="requestToken">The request token.</param>
    /// <returns>The approval URL.</returns>
    public static string ApprovalUrl(string requestToken)
        => string.Create(CultureInfo.InvariantCulture, $"https://www.themoviedb.org/authenticate/{requestToken}");
}
