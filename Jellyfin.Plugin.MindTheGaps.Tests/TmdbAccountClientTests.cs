using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using TMDbLib.Objects.Account;
using TMDbLib.Objects.Authentication;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Search;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

[Collection("PluginConfiguration")]
public class TmdbAccountClientTests
{
    [Fact]
    public void ApprovalUrl_UsesTheRequestTokenWithoutCallback()
    {
        Assert.Equal(
            "https://www.themoviedb.org/authenticate/request-token",
            TmdbAccountClient.ApprovalUrl("request-token"));
    }

    [Fact]
    public async Task CreateRequestToken_WhenNoOwnApiKeyIsConfigured_ReturnsNull()
    {
        using var client = new TmdbAccountClient(NullLogger<TmdbAccountClient>.Instance);

        var result = await client.CreateRequestTokenAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateSession_WhenNoOwnApiKeyIsConfigured_ReturnsNull()
    {
        using var client = new TmdbAccountClient(NullLogger<TmdbAccountClient>.Instance);

        var result = await client.CreateSessionAsync("approved-token", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AccountReads_WhenNoSessionIsConfigured_ReturnNull()
    {
        using var client = new TmdbAccountClient(NullLogger<TmdbAccountClient>.Instance);

        var account = await client.GetAccountNameAsync(CancellationToken.None);
        var movies = await client.GetMovieWatchlistAsync(10, CancellationToken.None);
        var series = await client.GetSeriesWatchlistAsync(10, CancellationToken.None);
        var favoriteMovies = await client.GetFavoriteMoviesAsync(10, CancellationToken.None);
        var favoriteSeries = await client.GetFavoriteSeriesAsync(10, CancellationToken.None);

        Assert.Null(account);
        Assert.Null(movies);
        Assert.Null(series);
        Assert.Null(favoriteMovies);
        Assert.Null(favoriteSeries);
    }

    [Fact]
    public async Task CreateRequestTokenAndSession_UsesInjectedAccountApi()
    {
        var api = new FakeAccountApi
        {
            Token = new Token { Success = true, RequestToken = "request-token" },
            Session = new UserSession { Success = true, SessionId = "session-id" }
        };
        InstallPluginConfiguration(new PluginConfiguration { TmdbApiKey = "own-key" });
        try
        {
            using var client = new TmdbAccountClient(NullLogger<TmdbAccountClient>.Instance, new FakeAccountApiFactory(api));

            Assert.Equal("request-token", await client.CreateRequestTokenAsync(CancellationToken.None));
            Assert.Equal("session-id", await client.CreateSessionAsync("request-token", CancellationToken.None));
            Assert.Equal("own-key", api.CreatedWithKey);
        }
        finally
        {
            SetPluginInstance(null);
        }
    }

    [Fact]
    public async Task GetAccountName_UsesConfiguredSession()
    {
        var api = new FakeAccountApi { Account = new AccountDetails { Username = "marc" } };
        InstallPluginConfiguration(new PluginConfiguration { TmdbApiKey = "own-key", TmdbSessionId = "session-id" });
        try
        {
            using var client = new TmdbAccountClient(NullLogger<TmdbAccountClient>.Instance, new FakeAccountApiFactory(api));

            Assert.Equal("marc", await client.GetAccountNameAsync(CancellationToken.None));
            Assert.Equal("session-id", api.SessionId);
            Assert.Equal(SessionType.UserSession, api.SessionType);
        }
        finally
        {
            SetPluginInstance(null);
        }
    }

    [Fact]
    public async Task GetMovieWatchlist_RespectsMaxItemsAcrossPages()
    {
        var api = new FakeAccountApi
        {
            MoviePages = new Dictionary<int, SearchContainer<SearchMovie>>
            {
                [1] = Page(new SearchMovie { Id = 1 }, new SearchMovie { Id = 2 }),
                [2] = Page(new SearchMovie { Id = 3 }, new SearchMovie { Id = 4 })
            }
        };
        InstallPluginConfiguration(new PluginConfiguration { TmdbApiKey = "own-key", TmdbSessionId = "session-id" });
        try
        {
            using var client = new TmdbAccountClient(NullLogger<TmdbAccountClient>.Instance, new FakeAccountApiFactory(api));

            var results = await client.GetMovieWatchlistAsync(3, CancellationToken.None);

            Assert.NotNull(results);
            Assert.Equal(new[] { 1, 2, 3 }, results!.Select(movie => movie.Id));
            Assert.Equal(new[] { 1, 2 }, api.MoviePagesRequested);
        }
        finally
        {
            SetPluginInstance(null);
        }
    }

    private static SearchContainer<T> Page<T>(params T[] results)
        => new() { Results = new List<T>(results), TotalPages = 2 };

    private static void InstallPluginConfiguration(PluginConfiguration config)
    {
        var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        SetPluginInstance(plugin);
        typeof(BasePlugin<PluginConfiguration>).GetProperty("Configuration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(plugin, config);
    }

    private static void SetPluginInstance(Plugin? plugin)
        => typeof(Plugin).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, plugin);

    private sealed class FakeAccountApiFactory : ITmdbAccountApiFactory
    {
        private readonly FakeAccountApi _api;

        public FakeAccountApiFactory(FakeAccountApi api) => _api = api;

        public ITmdbAccountApi Create(string apiKey)
        {
            _api.CreatedWithKey = apiKey;
            return _api;
        }
    }

    private sealed class FakeAccountApi : ITmdbAccountApi
    {
        public Token? Token { get; init; }

        public UserSession? Session { get; init; }

        public AccountDetails? Account { get; init; }

        public Dictionary<int, SearchContainer<SearchMovie>> MoviePages { get; init; } = [];

        public List<int> MoviePagesRequested { get; } = [];

        public string? CreatedWithKey { get; set; }

        public string? SessionId { get; private set; }

        public SessionType SessionType { get; private set; }

        public Task SetSessionInformationAsync(string sessionId, SessionType sessionType)
        {
            SessionId = sessionId;
            SessionType = sessionType;
            return Task.CompletedTask;
        }

        public Task<Token?> AuthenticationRequestAutenticationTokenAsync(CancellationToken cancellationToken)
            => Task.FromResult(Token);

        public Task<UserSession?> AuthenticationGetUserSessionAsync(string requestToken, CancellationToken cancellationToken)
            => Task.FromResult(Session);

        public Task<AccountDetails?> AccountGetDetailsAsync(CancellationToken cancellationToken)
            => Task.FromResult(Account);

        public Task<SearchContainer<SearchMovie>?> AccountGetMovieWatchlistAsync(int page, CancellationToken cancellationToken)
        {
            MoviePagesRequested.Add(page);
            return Task.FromResult(MoviePages.TryGetValue(page, out var result) ? result : null);
        }

        public Task<SearchContainer<SearchTv>?> AccountGetTvWatchlistAsync(int page, CancellationToken cancellationToken)
            => Task.FromResult<SearchContainer<SearchTv>?>(null);

        public Task<SearchContainer<SearchMovie>?> AccountGetFavoriteMoviesAsync(int page, CancellationToken cancellationToken)
            => Task.FromResult<SearchContainer<SearchMovie>?>(null);

        public Task<SearchContainer<SearchTv>?> AccountGetFavoriteTvAsync(int page, CancellationToken cancellationToken)
            => Task.FromResult<SearchContainer<SearchTv>?>(null);

        public void Dispose()
        {
        }
    }
}
