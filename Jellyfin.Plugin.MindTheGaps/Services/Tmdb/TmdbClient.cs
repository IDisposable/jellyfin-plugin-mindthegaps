using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Microsoft.Extensions.Caching.Memory;
using TMDbLib.Client;
using TMDbLib.Objects.Collections;
using TMDbLib.Objects.People;
using TMDbLib.Objects.Search;

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

    private readonly IMemoryCache _cache;
    private readonly TMDbClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbClient"/> class.
    /// </summary>
    /// <param name="cache">The shared memory cache.</param>
    public TmdbClient(IMemoryCache cache)
    {
        _cache = cache;

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

        return collection;
    }

    /// <summary>
    /// Gets a person with movie credits by their TMDB id.
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

        var person = await _client.GetPersonAsync(
            tmdbId,
            NormalizeLanguage(language, country),
            PersonMethods.MovieCredits,
            cancellationToken).ConfigureAwait(false);

        if (person is not null)
        {
            _cache.Set(key, person, TimeSpan.FromHours(CacheDurationHours));
        }

        return person;
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
        var results = await _client.GetMovieSimilarAsync(tmdbId, language, page, cancellationToken).ConfigureAwait(false);
        return results?.Results is null || results.Results.Count == 0
            ? (Array.Empty<SearchMovie>(), 0)
            : (results.Results, results.TotalPages);
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
        var results = await _client.GetTvShowSimilarAsync(tmdbId, language, page, cancellationToken).ConfigureAwait(false);
        return results?.Results is null || results.Results.Count == 0
            ? (Array.Empty<SearchTv>(), 0)
            : (results.Results, results.TotalPages);
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

        var results = await query.Query(page, cancellationToken).ConfigureAwait(false);
        return results?.Results is null || results.Results.Count == 0
            ? (Array.Empty<SearchMovie>(), 0)
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

        var results = await query.Query(page, cancellationToken).ConfigureAwait(false);
        return results?.Results is null || results.Results.Count == 0
            ? (Array.Empty<SearchMovie>(), 0)
            : (results.Results, results.TotalPages);
    }

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

        var results = await _client.SearchCompanyAsync(name, 0, cancellationToken).ConfigureAwait(false);
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
            return Array.Empty<CuratedSetRef>();
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"company-suggest-{query.ToUpperInvariant()}");
        if (_cache.TryGetValue(key, out IReadOnlyList<CuratedSetRef>? cached) && cached is not null)
        {
            return cached;
        }

        var results = await _client.SearchCompanyAsync(query, 0, cancellationToken).ConfigureAwait(false);
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
            return Array.Empty<CuratedSetRef>();
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"keyword-suggest-{query.ToUpperInvariant()}");
        if (_cache.TryGetValue(key, out IReadOnlyList<CuratedSetRef>? cached) && cached is not null)
        {
            return cached;
        }

        var results = await _client.SearchKeywordAsync(query, 0, cancellationToken).ConfigureAwait(false);
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
    /// Gets a company's (studio's) display name by its TMDB id, for labelling a curated set.
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

        var company = await _client.GetCompanyAsync(companyId, cancellationToken: cancellationToken).ConfigureAwait(false);
        var name = company?.Name;
        if (!string.IsNullOrEmpty(name))
        {
            _cache.Set(key, name, CachedApiClient.StableCacheDuration);
        }

        return name;
    }

    /// <summary>
    /// Gets a keyword's display name by its TMDB id, for labelling a curated set with its name rather than
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

        var keyword = await _client.GetKeywordAsync(keywordId, cancellationToken).ConfigureAwait(false);
        var name = keyword?.Name;
        if (!string.IsNullOrEmpty(name))
        {
            _cache.Set(key, name, CachedApiClient.StableCacheDuration);
        }

        return name;
    }

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
            var ids = await _client.GetTvShowExternalIdsAsync(tmdbId, cancellationToken).ConfigureAwait(false);
            if (ids is not null)
            {
                imdb = ids.ImdbId;
                tvdb = string.IsNullOrEmpty(ids.TvdbId) ? null : ids.TvdbId;
            }
        }
        else
        {
            var ids = await _client.GetMovieExternalIdsAsync(tmdbId, cancellationToken).ConfigureAwait(false);
            if (ids is not null)
            {
                imdb = ids.ImdbId;
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

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
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
