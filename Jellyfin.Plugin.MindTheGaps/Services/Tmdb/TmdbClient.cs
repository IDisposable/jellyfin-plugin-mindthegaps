using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TMDbLib.Client;
using TMDbLib.Objects.Collections;
using TMDbLib.Objects.Find;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.People;
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;

namespace Jellyfin.Plugin.MindTheGaps.Services.Tmdb;

/// <summary>
/// A self-contained TMDB client. Wraps <see cref="TMDbClient"/> (the public TMDbLib package) with a
/// small cache and a configurable key, so the plugin does not depend on the host's
/// <c>MediaBrowser.Providers</c> assembly and can build against the published Jellyfin NuGet packages.
/// </summary>
public sealed class TmdbClient : IDisposable
{
    /// <summary>
    /// The public TMDB API key shipped with the Jellyfin server (used when the user sets none).
    /// </summary>
    public const string DefaultApiKey = "4219e299c89411838049ab0dab19ebd5";

    private const int CacheDurationHours = 1;

    // The settings type-ahead shows a short list, so a partial query does not flood the dropdown.
    private const int MaxSuggestions = 10;

    private const string ImageBaseUrl = "https://image.tmdb.org/t/p/";
    private const string PosterSize = "w500";
    private const string BackdropSize = "w1280";
    private const string StillSize = "w300";

    private readonly IMemoryCache _cache;
    private readonly ILogger<TmdbClient>? _logger;
    private readonly TMDbClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbClient"/> class.
    /// </summary>
    /// <param name="cache">The shared memory cache.</param>
    /// <param name="logger">The logger, for the gated detailed-API logging. Optional so tests can omit it.</param>
    public TmdbClient(IMemoryCache cache, ILogger<TmdbClient>? logger = null)
    {
        _cache = cache;
        _logger = logger;

        // MaxRetryCount turns on TMDbLib's own handling of TMDB rate limiting (HTTP 429): it waits for the
        // response's Retry-After and retries, up to this many times, which is the TMDB analogue of the
        // HttpRetry policy the hand-rolled clients use.
        _client = new TMDbClient(ResolveApiKey()) { ThrowApiExceptions = false, MaxRetryCount = 3 };
    }

    /// <summary>
    /// Resolves the configured TMDB API key, falling back to the public default.
    /// </summary>
    /// <returns>The API key to use.</returns>
    public static string ResolveApiKey()
    {
        var key = Plugin.Instance?.Configuration.TmdbApiKey;
        return string.IsNullOrEmpty(key) ? DefaultApiKey : key;
    }

    // ******
    // People
    // ******

    /// <summary>
    /// Gets a person with their movie and TV credits by their TMDB id.
    /// </summary>
    /// <param name="tmdbId">The TMDB person id.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="country">The metadata country code.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The person, or <see langword="null"/>.</returns>
    public async Task<Person?> GetPersonAsync(int tmdbId, string? language, string? country, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"person-{tmdbId}-{language}");
        if (_cache.TryGetValue(key, out Person? cached))
        {
            return cached;
        }

        _logger.Detailed("TMDB: GetPerson {TmdbId} language {Language} country {Country}", tmdbId, language, country);
        var person = await _client.GetPersonAsync(
            tmdbId,
            NormalizeLanguage(language, country),
            PersonMethods.MovieCredits | PersonMethods.TvCredits,
            cancellationToken).ConfigureAwait(false);

        if (person is not null)
        {
            _cache.Set(key, person, TimeSpan.FromHours(CacheDurationHours));
        }
        else
        {
            _logger?.LogWarning("TMDB: GetPerson {TmdbId} returned nothing", tmdbId);
        }

        return person;
    }

    /// <summary>
    /// Resolves an IMDb name id ("nm0000229") to a TMDB person id, so a person named on an IMDb list can be
    /// handed to the same filmography pass an owned person goes through. An IMDb id never changes, so the
    /// answer is cached far longer than a catalog read.
    /// </summary>
    /// <param name="imdbNameId">The IMDb name id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The TMDB person id, or <see langword="null"/> when TMDB does not know the person.</returns>
    public async Task<int?> FindPersonByImdbIdAsync(string imdbNameId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imdbNameId))
        {
            return null;
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"findperson-{imdbNameId}");
        if (_cache.TryGetValue(key, out int? cached))
        {
            return cached;
        }

        _logger.Detailed("TMDB: FindPerson {ImdbId}", imdbNameId);
        var found = await _client.FindAsync(FindExternalSource.Imdb, imdbNameId, cancellationToken).ConfigureAwait(false);
        var personId = found?.PersonResults?.Count > 0 ? found.PersonResults[0].Id : (int?)null;

        if (personId is not null)
        {
            _cache.Set(key, personId, TimeSpan.FromDays(30));
        }
        else
        {
            _logger?.LogWarning("TMDB: FindPerson {ImdbId} matched no person", imdbNameId);
        }

        return personId;
    }

    // ******
    // Movies
    // ******

    /// <summary>
    /// Gets a collection (movie franchise) by its TMDB id.
    /// </summary>
    /// <param name="tmdbId">The TMDB collection id.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="country">The metadata country code.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The collection, or <see langword="null"/>.</returns>
    public async Task<Collection?> GetCollectionAsync(int tmdbId, string? language, string? country, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"collection-{tmdbId}-{language}");
        if (_cache.TryGetValue(key, out Collection? cached))
        {
            return cached;
        }

        _logger.Detailed("TMDB: GetCollection {TmdbId} lang {Language}", tmdbId, language);
        var collection = await _client.GetCollectionAsync(
            tmdbId,
            NormalizeLanguage(language, country),
            null,
            CollectionMethods.Undefined,
            cancellationToken).ConfigureAwait(false);

        if (collection is not null)
        {
            _cache.Set(key, collection, TimeSpan.FromHours(CacheDurationHours));
        }
        else
        {
            _logger?.LogWarning("TMDB: GetCollection {TmdbId} returned nothing", tmdbId);
        }

        return collection;
    }

    /// <summary>
    /// Gets a movie's details (overview, runtime, genres, rating, external ids, videos) by its TMDB id, for a
    /// detail view of an unowned title.
    /// </summary>
    /// <param name="tmdbId">The TMDB movie id.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="country">The metadata country code.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The movie, or <see langword="null"/>.</returns>
    public async Task<Movie?> GetMovieDetailsAsync(int tmdbId, string? language, string? country, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"moviedetails-{tmdbId}-{language}");
        if (_cache.TryGetValue(key, out Movie? cached))
        {
            return cached;
        }

        _logger.Detailed("TMDB: GetMovie {TmdbId} language {Language} country {Country}", tmdbId, language, country);
        var movie = await _client.GetMovieAsync(
            tmdbId,
            NormalizeLanguage(language, country),
            null,
            MovieMethods.ExternalIds | MovieMethods.Videos,
            cancellationToken).ConfigureAwait(false);

        if (movie is not null)
        {
            _cache.Set(key, movie, TimeSpan.FromHours(CacheDurationHours));
        }
        else
        {
            _logger?.LogWarning("TMDB: GetMovie {TmdbId} returned nothing", tmdbId);
        }

        return movie;
    }

    /// <summary>
    /// Gets a single page of similar movies for a movie.
    /// </summary>
    /// <param name="tmdbId">The TMDB movie id.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results and the total page count.</returns>
    public async Task<(IReadOnlyList<SearchMovie> Results, int TotalPages)> GetMovieSimilarPageAsync(int tmdbId, int page, string? language, CancellationToken cancellationToken)
    {
        _logger.Detailed("TMDB: GetMovieSimilar {TmdbId} page {Page} lang {Language}", tmdbId, page, language);
        var results = await _client.GetMovieSimilarAsync(tmdbId, language, page, cancellationToken).ConfigureAwait(false);
        if (results?.Results is null)
        {
            _logger?.LogWarning("TMDB: GetMovieSimilar {TmdbId} page {Page} returned nothing", tmdbId, page);
            return ([], 0);
        }

        return results.Results.Count == 0
            ? ([], 0)
            : (results.Results, results.TotalPages);
    }

    /// <summary>
    /// Gets TMDB's recommendations for a movie: the titles TMDB's users who liked this one also liked. Far
    /// closer to "more like this" than the <c>similar</c> endpoint, which matches on keywords and genres and
    /// returns obscure titles for well-known films. First page only, cached, since a page view asks for it.
    /// </summary>
    /// <param name="tmdbId">The TMDB movie id.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The recommended movies, most relevant first; empty when TMDB has none.</returns>
    public async Task<IReadOnlyList<SearchMovie>> GetMovieRecommendationsAsync(int tmdbId, string? language, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"movierecs-{tmdbId}-{language}");
        if (_cache.TryGetValue(key, out IReadOnlyList<SearchMovie>? cached) && cached is not null)
        {
            return cached;
        }

        _logger.Detailed("TMDB: GetMovieRecommendations {TmdbId} lang {Language}", tmdbId, language);
        var movie = await _client.GetMovieAsync(tmdbId, NormalizeLanguage(language, null), null, MovieMethods.Recommendations, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SearchMovie> results = movie?.Recommendations?.Results ?? [];
        if (movie is null)
        {
            _logger?.LogWarning("TMDB: GetMovieRecommendations {TmdbId} returned nothing", tmdbId);
        }

        _cache.Set(key, results, TimeSpan.FromHours(CacheDurationHours));
        return results;
    }

    /// <summary>
    /// Searches TMDB's movies by title, first page, for the watchlist search.
    /// </summary>
    /// <param name="query">The title, or part of it.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matches, TMDB's best first.</returns>
    public async Task<IReadOnlyList<SearchMovie>> SearchMoviesAsync(string query, string? language, CancellationToken cancellationToken)
    {
        _logger.Detailed("TMDB: SearchMovie '{Query}' lang {Language}", query, language);
        var page = await _client.SearchMovieAsync(query, NormalizeLanguage(language, null), 1, false, 0, null, 0, cancellationToken).ConfigureAwait(false);
        return page?.Results ?? [];
    }

    /// <summary>
    /// Gets a single page of a company's (studio's) movies via discover.
    /// </summary>
    /// <param name="companyId">The TMDB company id.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results and the total page count.</returns>
    public async Task<(IReadOnlyList<SearchMovie> Results, int TotalPages)> DiscoverMoviesByCompanyAsync(int companyId, int page, string? language, CancellationToken cancellationToken)
    {
        var query = _client.DiscoverMoviesAsync().IncludeWithAllOfCompany(new[] { companyId });
        if (!string.IsNullOrEmpty(language))
        {
            query = query.WhereLanguageIs(language);
        }

        _logger.Detailed("TMDB: DiscoverMoviesByCompany {CompanyId} page {Page} lang {Language}", companyId, page, language);
        var results = await query.Query(page, cancellationToken).ConfigureAwait(false);
        if (results?.Results is null)
        {
            _logger?.LogWarning("TMDB: DiscoverMoviesByCompany {CompanyId} page {Page} returned nothing", companyId, page);
            return ([], 0);
        }

        return results.Results.Count == 0
            ? ([], 0)
            : (results.Results, results.TotalPages);
    }

    /// <summary>
    /// Gets a single page of a keyword's movies via discover.
    /// </summary>
    /// <param name="keywordId">The TMDB keyword id.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results and the total page count.</returns>
    public async Task<(IReadOnlyList<SearchMovie> Results, int TotalPages)> DiscoverMoviesByKeywordAsync(int keywordId, int page, string? language, CancellationToken cancellationToken)
    {
        var query = _client.DiscoverMoviesAsync().IncludeWithAllOfKeywords(new[] { keywordId });
        if (!string.IsNullOrEmpty(language))
        {
            query = query.WhereLanguageIs(language);
        }

        _logger.Detailed("TMDB: DiscoverMoviesByKeyword {KeywordId} page {Page} lang {Language}", keywordId, page, language);
        var results = await query.Query(page, cancellationToken).ConfigureAwait(false);
        if (results?.Results is null)
        {
            _logger?.LogWarning("TMDB: DiscoverMoviesByKeyword {KeywordId} page {Page} returned nothing", keywordId, page);
            return ([], 0);
        }

        return results.Results.Count == 0
            ? ([], 0)
            : (results.Results, results.TotalPages);
    }

    /// <summary>
    /// Gets a TMDB list's movie members and the list's display name. The list is fetched whole (TMDB lists
    /// are not paginated); non-movie members are ignored, since the curated-set source is movies only.
    /// </summary>
    /// <param name="listId">The TMDB list id.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list name (or null) and its movie members.</returns>
    public async Task<(string? Name, IReadOnlyList<SearchMovie> Movies)> GetListMoviesAsync(int listId, string? language, CancellationToken cancellationToken)
    {
        var idText = listId.ToString(CultureInfo.InvariantCulture);
        _logger.Detailed("TMDB: GetList {ListId} lang {Language}", idText, language);
        var list = await _client.GetListAsync(idText, language, cancellationToken).ConfigureAwait(false);
        if (list is null)
        {
            _logger?.LogWarning("TMDB: GetList {ListId} returned nothing", idText);
        }

        if (list?.Items is null || list.Items.Count == 0)
        {
            return (list?.Name, []);
        }

        var movies = new List<SearchMovie>();
        foreach (var item in list.Items)
        {
            if (item is SearchMovie movie)
            {
                movies.Add(movie);
            }
        }

        return (list.Name, movies);
    }

    /// <summary>
    /// Gets a single page of TMDB's official "Top Rated" movie feed.
    /// </summary>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="region">The region the ranking is scoped to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results and the total page count.</returns>
    public Task<(IReadOnlyList<SearchMovie> Results, int TotalPages)> GetTopRatedMoviesAsync(int page, string? language, string? region, CancellationToken cancellationToken)
        => DiscoverFeedAsync("TopRated", async (l, p, r, ct) => await _client.GetMovieTopRatedListAsync(l, p, r, ct).ConfigureAwait(false), page, language, region, cancellationToken);

    /// <summary>
    /// Gets a single page of TMDB's official "Popular" movie feed.
    /// </summary>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="region">The region the ranking is scoped to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results and the total page count.</returns>
    public Task<(IReadOnlyList<SearchMovie> Results, int TotalPages)> GetPopularMoviesAsync(int page, string? language, string? region, CancellationToken cancellationToken)
        => DiscoverFeedAsync("Popular", async (l, p, r, ct) => await _client.GetMoviePopularListAsync(l, p, r, ct).ConfigureAwait(false), page, language, region, cancellationToken);

    /// <summary>
    /// Gets a single page of TMDB's official "Upcoming" movie feed.
    /// </summary>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="region">The region the release window is scoped to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results and the total page count.</returns>
    public Task<(IReadOnlyList<SearchMovie> Results, int TotalPages)> GetUpcomingMoviesAsync(int page, string? language, string? region, CancellationToken cancellationToken)
        => DiscoverFeedAsync("Upcoming", async (l, p, r, ct) => await _client.GetMovieUpcomingListAsync(l, p, r, ct).ConfigureAwait(false), page, language, region, cancellationToken);

    /// <summary>
    /// Gets a single page of TMDB's official "Now Playing" movie feed.
    /// </summary>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="region">The region the release window is scoped to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results and the total page count.</returns>
    public Task<(IReadOnlyList<SearchMovie> Results, int TotalPages)> GetNowPlayingMoviesAsync(int page, string? language, string? region, CancellationToken cancellationToken)
        => DiscoverFeedAsync("NowPlaying", async (l, p, r, ct) => await _client.GetMovieNowPlayingListAsync(l, p, r, ct).ConfigureAwait(false), page, language, region, cancellationToken);

    // *****
    // Shows
    // *****

    /// <summary>
    /// Gets a series' details (overview, seasons, genres, rating, network, external ids, videos) by its TMDB
    /// id, for a detail view of an unowned title.
    /// </summary>
    /// <param name="tmdbId">The TMDB series id.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="country">The metadata country code.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The series, or <see langword="null"/>.</returns>
    public async Task<TvShow?> GetSeriesDetailsAsync(int tmdbId, string? language, string? country, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"seriesdetails-{tmdbId}-{language}");
        if (_cache.TryGetValue(key, out TvShow? cached))
        {
            return cached;
        }

        _logger.Detailed("TMDB: GetTvShow {TmdbId} language {Language} country {Country}", tmdbId, language, country);
        var show = await _client.GetTvShowAsync(
            tmdbId,
            TvShowMethods.ExternalIds | TvShowMethods.Videos,
            NormalizeLanguage(language, country),
            null,
            cancellationToken).ConfigureAwait(false);

        if (show is not null)
        {
            _cache.Set(key, show, TimeSpan.FromHours(CacheDurationHours));
        }
        else
        {
            _logger?.LogWarning("TMDB: GetTvShow {TmdbId} returned nothing", tmdbId);
        }

        return show;
    }

    /// <summary>
    /// Searches TMDB's series by name, first page, for the watchlist search.
    /// </summary>
    /// <param name="query">The name, or part of it.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matches, TMDB's best first.</returns>
    public async Task<IReadOnlyList<SearchTv>> SearchSeriesAsync(string query, string? language, CancellationToken cancellationToken)
    {
        _logger.Detailed("TMDB: SearchTvShow '{Query}' lang {Language}", query, language);
        var page = await _client.SearchTvShowAsync(query, NormalizeLanguage(language, null), 1, false, 0, cancellationToken).ConfigureAwait(false);
        return page?.Results ?? [];
    }

    /// <summary>
    /// Gets a single page of similar shows for a series.
    /// </summary>
    /// <param name="tmdbId">The TMDB series id.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The results and the total page count.</returns>
    public async Task<(IReadOnlyList<SearchTv> Results, int TotalPages)> GetSeriesSimilarPageAsync(int tmdbId, int page, string? language, CancellationToken cancellationToken)
    {
        _logger.Detailed("TMDB: GetSeriesSimilar {TmdbId} page {Page} lang {Language}", tmdbId, page, language);
        var results = await _client.GetTvShowSimilarAsync(tmdbId, language, page, cancellationToken).ConfigureAwait(false);
        if (results?.Results is null)
        {
            _logger?.LogWarning("TMDB: GetSeriesSimilar {TmdbId} page {Page} returned nothing", tmdbId, page);
            return ([], 0);
        }

        return results.Results.Count == 0
            ? ([], 0)
            : (results.Results, results.TotalPages);
    }

    /// <summary>
    /// Gets TMDB's recommendations for a series; see <see cref="GetMovieRecommendationsAsync"/>.
    /// </summary>
    /// <param name="tmdbId">The TMDB series id.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The recommended series, most relevant first; empty when TMDB has none.</returns>
    public async Task<IReadOnlyList<SearchTv>> GetSeriesRecommendationsAsync(int tmdbId, string? language, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"seriesrecs-{tmdbId}-{language}");
        if (_cache.TryGetValue(key, out IReadOnlyList<SearchTv>? cached) && cached is not null)
        {
            return cached;
        }

        _logger.Detailed("TMDB: GetSeriesRecommendations {TmdbId} lang {Language}", tmdbId, language);
        var page = await _client.GetTvShowRecommendationsAsync(tmdbId, NormalizeLanguage(language, null), 1, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SearchTv> results = page?.Results ?? [];
        if (page is null)
        {
            _logger?.LogWarning("TMDB: GetSeriesRecommendations {TmdbId} returned nothing", tmdbId);
        }

        _cache.Set(key, results, TimeSpan.FromHours(CacheDurationHours));
        return results;
    }

    /// <summary>
    /// Gets every regular (numbered) episode of a series across its seasons, for the series-content
    /// cross-check. Specials (season 0) are skipped. Cached, since the same series is re-read across scans
    /// and re-checks.
    /// </summary>
    /// <param name="tmdbId">The series' TMDB id.</param>
    /// <param name="language">The metadata language.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The season episodes, or an empty list when the series could not be fetched.</returns>
    public async Task<IReadOnlyList<TvSeasonEpisode>> GetSeriesEpisodesAsync(int tmdbId, string? language, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"tvepisodes-{tmdbId}-{language}");
        if (_cache.TryGetValue(key, out IReadOnlyList<TvSeasonEpisode>? cached) && cached is not null)
        {
            return cached;
        }

        _logger.Detailed("TMDB: GetSeriesEpisodes {TmdbId} lang {Language}", tmdbId, language);
        var show = await _client.GetTvShowAsync(tmdbId, language: language, cancellationToken: cancellationToken).ConfigureAwait(false);
        var episodes = new List<TvSeasonEpisode>();
        if (show?.Seasons is null || show.Seasons.Count == 0)
        {
            _logger?.LogWarning("TMDB: GetSeriesEpisodes {TmdbId} returned no seasons", tmdbId);
            return episodes;
        }

        foreach (var season in show.Seasons)
        {
            if (season.SeasonNumber < 1)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var full = await _client.GetTvSeasonAsync(tmdbId, season.SeasonNumber, language: language, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (full?.Episodes is { Count: > 0 })
            {
                episodes.AddRange(full.Episodes);
            }
        }

        _cache.Set(key, (IReadOnlyList<TvSeasonEpisode>)episodes, TimeSpan.FromHours(CacheDurationHours));
        return episodes;
    }

    // *********
    // Companies
    // *********

    /// <summary>
    /// Resolves a studio/company name to its best-match TMDB company id and canonical name (cached, and a
    /// null result is cached too). Returns null when there is no match.
    /// </summary>
    /// <param name="name">The studio name to search for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matched company id and name, or null.</returns>
    public async Task<(int Id, string Name)?> SearchCompanyAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"company-search-{name.ToUpperInvariant()}");
        if (_cache.TryGetValue(key, out (int Id, string Name)? cached))
        {
            return cached;
        }

        _logger.Detailed("TMDB: SearchCompany {Name}", name);
        var results = await _client.SearchCompanyAsync(name, 0, cancellationToken).ConfigureAwait(false);
        if (results?.Results is null)
        {
            _logger?.LogWarning("TMDB: SearchCompany {Name} returned nothing", name);
        }

        var first = results?.Results is { Count: > 0 } list ? list[0] : null;
        (int Id, string Name)? match = first is null ? null : (first.Id, string.IsNullOrEmpty(first.Name) ? name : first.Name);
        _cache.Set(key, match, TimeSpan.FromHours(CacheDurationHours));
        return match;
    }

    /// <summary>
    /// Searches TMDB studios (companies) by name for the settings type-ahead, returning the top matches as
    /// id and name pairs (the empty result is cached too).
    /// </summary>
    /// <param name="query">The partial studio name typed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The top matches.</returns>
    public async Task<IReadOnlyList<CuratedSetRef>> SearchCompaniesAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"company-suggest-{query.ToUpperInvariant()}");
        if (_cache.TryGetValue(key, out IReadOnlyList<CuratedSetRef>? cached) && cached is not null)
        {
            return cached;
        }

        _logger.Detailed("TMDB: SearchCompanies {Query}", query);
        var results = await _client.SearchCompanyAsync(query, 0, cancellationToken).ConfigureAwait(false);
        if (results?.Results is null)
        {
            _logger?.LogWarning("TMDB: SearchCompanies {Query} returned nothing", query);
        }

        var refs = new List<CuratedSetRef>();
        foreach (var company in results?.Results ?? new List<SearchCompany>())
        {
            if (!string.IsNullOrEmpty(company.Name))
            {
                refs.Add(new CuratedSetRef { Id = company.Id, Name = company.Name });
            }

            if (refs.Count >= MaxSuggestions)
            {
                break;
            }
        }

        _cache.Set(key, (IReadOnlyList<CuratedSetRef>)refs, TimeSpan.FromHours(CacheDurationHours));
        return refs;
    }

    /// <summary>
    /// Gets a company's (studio's) display name by its TMDB id, for labeling a curated set.
    /// </summary>
    /// <param name="companyId">The TMDB company id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The company name, or null if not found.</returns>
    public async Task<string?> GetCompanyNameAsync(int companyId, CancellationToken cancellationToken)
    {
        // A studio name is stable, so cache it well beyond a scan. This also backs the chip picker's
        // CuratedResolve, which would otherwise re-fetch every name each time the settings page loads.
        var key = string.Create(CultureInfo.InvariantCulture, $"tmdb:companyname:{companyId}");
        if (_cache.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        _logger.Detailed("TMDB: GetCompany {CompanyId}", companyId);
        var company = await _client.GetCompanyAsync(companyId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            _logger?.LogWarning("TMDB: GetCompany {CompanyId} returned nothing", companyId);
        }

        var name = company?.Name;
        if (!string.IsNullOrEmpty(name))
        {
            _cache.Set(key, name, CachedApiClient.StableCacheDuration);
        }

        return name;
    }

    // ********
    // Keywords
    // ********

    /// <summary>
    /// Searches TMDB keywords by name for the settings type-ahead, returning the top matches as id and name
    /// pairs (the empty result is cached too). The type-ahead is how a keyword set is chosen without ever
    /// exposing its numeric id.
    /// </summary>
    /// <param name="query">The partial keyword typed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The top matches.</returns>
    public async Task<IReadOnlyList<CuratedSetRef>> SearchKeywordsAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"keyword-suggest-{query.ToUpperInvariant()}");
        if (_cache.TryGetValue(key, out IReadOnlyList<CuratedSetRef>? cached) && cached is not null)
        {
            return cached;
        }

        _logger.Detailed("TMDB: SearchKeywords {Query}", query);
        var results = await _client.SearchKeywordAsync(query, 0, cancellationToken).ConfigureAwait(false);
        if (results?.Results is null)
        {
            _logger?.LogWarning("TMDB: SearchKeywords {Query} returned nothing", query);
        }

        var refs = new List<CuratedSetRef>();
        foreach (var keyword in results?.Results ?? new List<SearchKeyword>())
        {
            if (!string.IsNullOrEmpty(keyword.Name))
            {
                refs.Add(new CuratedSetRef { Id = keyword.Id, Name = keyword.Name });
            }

            if (refs.Count >= MaxSuggestions)
            {
                break;
            }
        }

        _cache.Set(key, (IReadOnlyList<CuratedSetRef>)refs, TimeSpan.FromHours(CacheDurationHours));
        return refs;
    }

    /// <summary>
    /// Gets a keyword's display name by its TMDB id, for labeling a curated set with its name rather than
    /// its raw id.
    /// </summary>
    /// <param name="keywordId">The TMDB keyword id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The keyword name, or null if not found.</returns>
    public async Task<string?> GetKeywordNameAsync(int keywordId, CancellationToken cancellationToken)
    {
        // A keyword name is stable, so cache it well beyond a scan (same rationale as the company name).
        var key = string.Create(CultureInfo.InvariantCulture, $"tmdb:keywordname:{keywordId}");
        if (_cache.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        _logger.Detailed("TMDB: GetKeyword {KeywordId}", keywordId);
        var keyword = await _client.GetKeywordAsync(keywordId, cancellationToken).ConfigureAwait(false);
        if (keyword is null)
        {
            _logger?.LogWarning("TMDB: GetKeyword {KeywordId} returned nothing", keywordId);
        }

        var name = keyword?.Name;
        if (!string.IsNullOrEmpty(name))
        {
            _cache.Set(key, name, CachedApiClient.StableCacheDuration);
        }

        return name;
    }

    // ************
    // External IDs
    // ************

    /// <summary>
    /// Gets a title's external ids (IMDb, and TheTVDB for series) by its TMDB id. TMDB list responses
    /// only carry the TMDB id, so this fills in the rest so a gap can link to more than TMDB.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="isSeries">Whether the id is a series (otherwise a movie).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The IMDb id and (for series) the TheTVDB id, each null when absent.</returns>
    public async Task<(string? Imdb, string? Tvdb)> GetExternalIdsAsync(int tmdbId, bool isSeries, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"externalids-{(isSeries ? "tv" : "movie")}-{tmdbId}");
        if (_cache.TryGetValue(key, out (string? Imdb, string? Tvdb) cached))
        {
            return cached;
        }

        string? imdb = null;
        string? tvdb = null;
        if (isSeries)
        {
            _logger.Detailed("TMDB: GetSeriesExternalIds {TmdbId}", tmdbId);
            var ids = await _client.GetTvShowExternalIdsAsync(tmdbId, cancellationToken).ConfigureAwait(false);
            if (ids is not null)
            {
                imdb = ids.ImdbId;
                tvdb = string.IsNullOrEmpty(ids.TvdbId) ? null : ids.TvdbId;
            }
            else
            {
                _logger?.LogWarning("TMDB: GetSeriesExternalIds {TmdbId} returned nothing", tmdbId);
            }
        }
        else
        {
            _logger.Detailed("TMDB: GetMovieExternalIds {TmdbId}", tmdbId);
            var ids = await _client.GetMovieExternalIdsAsync(tmdbId, cancellationToken).ConfigureAwait(false);
            if (ids is not null)
            {
                imdb = ids.ImdbId;
            }
            else
            {
                _logger?.LogWarning("TMDB: GetMovieExternalIds {TmdbId} returned nothing", tmdbId);
            }
        }

        var result = (Imdb: string.IsNullOrEmpty(imdb) ? null : imdb, Tvdb: tvdb);
        _cache.Set(key, result, TimeSpan.FromHours(CacheDurationHours));
        return result;
    }

    /// <summary>
    /// Builds an absolute poster URL from a TMDB poster path.
    /// </summary>
    /// <param name="posterPath">The relative poster path.</param>
    /// <returns>The absolute URL, or <see langword="null"/>.</returns>
    public string? GetPosterUrl(string? posterPath)
        => string.IsNullOrEmpty(posterPath) ? null : ImageBaseUrl + PosterSize + posterPath;

    /// <summary>
    /// Resolves a TMDB backdrop path to a URL.
    /// </summary>
    /// <param name="backdropPath">The backdrop path.</param>
    /// <returns>The URL, or <see langword="null"/>.</returns>
    public string? GetBackdropUrl(string? backdropPath)
        => string.IsNullOrEmpty(backdropPath) ? null : ImageBaseUrl + BackdropSize + backdropPath;

    /// <summary>
    /// Resolves a TMDB episode still path to a URL. Its own (smaller) size preset, since a still is
    /// shown at thumbnail size in the report, the same as a poster.
    /// </summary>
    /// <param name="stillPath">The relative still path.</param>
    /// <returns>The absolute URL, or <see langword="null"/>.</returns>
    public string? GetStillUrl(string? stillPath)
        => string.IsNullOrEmpty(stillPath) ? null : ImageBaseUrl + StillSize + stillPath;

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
    }

    // Shared by the four official-feed wrappers above: they return different TMDbLib types
    // (SearchContainerWithDates for the two date-scoped feeds, plain SearchContainer for the other two),
    // but both expose Results/TotalPages through the common SearchContainer<T> base, so one helper covers
    // all four once each caller's own async lambda upcasts its await to it.
    private async Task<(IReadOnlyList<SearchMovie> Results, int TotalPages)> DiscoverFeedAsync(
        string feedName,
        Func<string?, int, string?, CancellationToken, Task<SearchContainer<SearchMovie>?>> fetch,
        int page,
        string? language,
        string? region,
        CancellationToken cancellationToken)
    {
        _logger.Detailed("TMDB: {Feed} page {Page} lang {Language} region {Region}", feedName, page, language, region);
        var results = await fetch(language, page, region, cancellationToken).ConfigureAwait(false);
        if (results?.Results is null)
        {
            _logger?.LogWarning("TMDB: {Feed} page {Page} returned nothing", feedName, page);
            return ([], 0);
        }

        return results.Results.Count == 0
            ? ([], 0)
            : (results.Results, results.TotalPages);
    }

    private static string? NormalizeLanguage(string? language, string? country)
    {
        if (string.IsNullOrEmpty(language) || language.Contains('-', StringComparison.Ordinal))
        {
            return language;
        }

        return string.IsNullOrEmpty(country)
            ? language
            : string.Create(CultureInfo.InvariantCulture, $"{language}-{country.ToUpperInvariant()}");
    }
}
