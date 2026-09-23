using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Backs the want-to-watch row's title search: TMDB search for a movie or series no page already lists, so
/// it can be added directly rather than only ever discovered through a person, item, or home page. Independent
/// of the scan; every result is checked against the same ownership index the other on-demand surfaces use.
/// </summary>
public sealed class WatchlistSearchService
{
    private const int MaxResults = 20;

    private static readonly BaseItemKind[] _kinds = [BaseItemKind.Movie, BaseItemKind.Series];

    private readonly TmdbClient _tmdb;
    private readonly OwnershipIndexBuilder _ownershipIndexBuilder;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WatchlistSearchService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistSearchService"/> class.
    /// </summary>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="ownershipIndexBuilder">Indexes the owned movies and series.</param>
    /// <param name="cache">The memory cache.</param>
    /// <param name="logger">The logger.</param>
    public WatchlistSearchService(TmdbClient tmdb, OwnershipIndexBuilder ownershipIndexBuilder, IMemoryCache cache, ILogger<WatchlistSearchService> logger)
    {
        _tmdb = tmdb;
        _ownershipIndexBuilder = ownershipIndexBuilder;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Searches TMDB for an unowned movie or series matching the query.
    /// </summary>
    /// <param name="kind">"Movie" or "Series".</param>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching unowned titles, or empty for a blank query or an unrecognized kind.</returns>
    public async Task<IReadOnlyList<MissingTitle>> SearchAsync(string kind, string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var config = Plugin.RequireConfiguration();
        var ownership = Ownership();
        IEnumerable<GapItem> gaps;
        if (string.Equals(kind, "Series", StringComparison.OrdinalIgnoreCase))
        {
            var results = await _tmdb.SearchSeriesAsync(query, config.MetadataLanguage, cancellationToken).ConfigureAwait(false);
            gaps = WatchlistSearchMapper.SearchSeries(results, ownership, _tmdb.GetPosterUrl, MaxResults);
        }
        else if (string.Equals(kind, "Movie", StringComparison.OrdinalIgnoreCase))
        {
            var results = await _tmdb.SearchMoviesAsync(query, config.MetadataLanguage, cancellationToken).ConfigureAwait(false);
            gaps = WatchlistSearchMapper.SearchMovies(results, ownership, _tmdb.GetPosterUrl, MaxResults);
        }
        else
        {
            return [];
        }

        _logger.LogDebug("Watchlist search: '{Query}' ({Kind})", query, kind);
        return gaps.Select(g => MissingTitleBuilder.ToTitle(g, null, null)).Where(t => t is not null).Select(t => t!).ToList();
    }

    /// <summary>
    /// Rehydrates a chosen search result by its own TMDB id, for a want-to-watch add. Looked up fresh from
    /// TMDB's detail record rather than trusted from the client, and rechecked against ownership in case the
    /// title arrived in the library between the search and the add.
    /// </summary>
    /// <param name="kind">"Movie" or "Series".</param>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The gap, or <see langword="null"/> when the kind is unrecognized, the id does not resolve, or the library already owns it.</returns>
    public async Task<GapItem?> FindGapAsync(string kind, int tmdbId, CancellationToken cancellationToken)
    {
        if (tmdbId <= 0)
        {
            return null;
        }

        var config = Plugin.RequireConfiguration();
        var ownership = Ownership();
        if (string.Equals(kind, "Series", StringComparison.OrdinalIgnoreCase))
        {
            var show = await _tmdb.GetSeriesDetailsAsync(tmdbId, config.MetadataLanguage, config.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
            return show is null ? null : WatchlistSearchMapper.FromSeriesDetails(show, ownership, _tmdb.GetPosterUrl);
        }

        if (string.Equals(kind, "Movie", StringComparison.OrdinalIgnoreCase))
        {
            var movie = await _tmdb.GetMovieDetailsAsync(tmdbId, config.MetadataLanguage, config.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
            return movie is null ? null : WatchlistSearchMapper.FromMovieDetails(movie, ownership, _tmdb.GetPosterUrl);
        }

        return null;
    }

    private OwnershipIndex Ownership()
    {
        if (!_cache.TryGetValue(OwnershipCache.Key, out OwnershipIndex? ownership) || ownership is null)
        {
            ownership = _ownershipIndexBuilder.Build(_kinds);
            _cache.Set(OwnershipCache.Key, ownership, OwnershipCache.Ttl);
        }

        return ownership;
    }
}
