using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Finds a title by name for the watchlist, whether the library has it or not: TMDB's movie and series
/// matches, each marked as owned (with its library item, so the card can link to it and bookmark it on the
/// playlist) or missing (so the card can bookmark it on the todo list or send it for download). The way to
/// get a title that is not out yet onto the list.
/// </summary>
public sealed class WatchlistSearchService
{
    private const int MaxResults = 20;
    private static readonly BaseItemKind[] _kinds = [BaseItemKind.Movie, BaseItemKind.Series];

    private readonly TmdbClient _tmdb;
    private readonly ILibraryManager _libraryManager;
    private readonly OwnershipIndexBuilder _ownershipIndexBuilder;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistSearchService"/> class.
    /// </summary>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="ownershipIndexBuilder">Indexes the owned movies and series.</param>
    /// <param name="cache">The memory cache.</param>
    public WatchlistSearchService(TmdbClient tmdb, ILibraryManager libraryManager, OwnershipIndexBuilder ownershipIndexBuilder, IMemoryCache cache)
    {
        _tmdb = tmdb;
        _libraryManager = libraryManager;
        _ownershipIndexBuilder = ownershipIndexBuilder;
        _cache = cache;
    }

    /// <summary>
    /// Searches.
    /// </summary>
    /// <param name="query">The title, or part of it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Movies then series, each in TMDB's order, owned ones marked.</returns>
    public async Task<IReadOnlyList<MissingTitle>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length < 2)
        {
            return [];
        }

        var config = Plugin.RequireConfiguration();
        var movies = await _tmdb.SearchMoviesAsync(trimmed, config.MetadataLanguage, cancellationToken).ConfigureAwait(false);
        var series = await _tmdb.SearchSeriesAsync(trimmed, config.MetadataLanguage, cancellationToken).ConfigureAwait(false);
        var ownership = await _cache.GetOrCreateAsync(OwnershipCache.Key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = OwnershipCache.Ttl;
            return Task.FromResult(_ownershipIndexBuilder.Build(_kinds));
        }).ConfigureAwait(false);

        // The mapper is asked with an empty ownership index so owned titles come through too; ownership is
        // decided here, where the owned item's id can be attached.
        var none = new OwnershipIndex(new HashSet<string>(StringComparer.Ordinal));
        var gaps = RecommendationGapMapper.BuildMovies(movies, "search", null, null, none, _tmdb.GetPosterUrl, MaxResults, 0)
            .Concat(RecommendationGapMapper.BuildSeries(series, "search", null, null, none, _tmdb.GetPosterUrl, MaxResults, 0));

        var titles = new List<MissingTitle>();
        foreach (var gap in gaps)
        {
            var title = MissingTitleBuilder.ToTitle(gap, null, null);
            if (title is null)
            {
                continue;
            }

            if (ownership!.OwnsAny(gap.TargetKind, gap.ProviderIds))
            {
                title.OwnedItemId = FindOwned(gap)?.Id.ToString("N", CultureInfo.InvariantCulture);
            }

            titles.Add(title);
        }

        return titles;
    }

    /// <summary>
    /// Rebuilds a gap from a search card's id, from TMDB's record, for the detail dialog, a bookmark or a Send.
    /// </summary>
    /// <param name="gapId">The card's gap id (<c>recommendation:movie:{tmdbId}</c> or <c>recommendation:series:{tmdbId}</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The gap, or <see langword="null"/> when the id is not of that shape or TMDB has no such title.</returns>
    public async Task<GapItem?> FindGapAsync(string gapId, CancellationToken cancellationToken)
    {
        if (!TryParseId(gapId, out var kind, out var tmdbId))
        {
            return null;
        }

        var config = Plugin.RequireConfiguration();
        if (kind == BaseItemKind.Movie)
        {
            var movie = await _tmdb.GetMovieDetailsAsync(tmdbId, config.MetadataLanguage, config.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
            return movie is null ? null : Gap(gapId, BaseItemKind.Movie, MediaDomain.Movies, movie.Title, movie.ReleaseDate, tmdbId, _tmdb.GetPosterUrl(movie.PosterPath), movie.Overview);
        }

        var show = await _tmdb.GetSeriesDetailsAsync(tmdbId, config.MetadataLanguage, config.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
        return show is null ? null : Gap(gapId, BaseItemKind.Series, MediaDomain.Shows, show.Name, show.FirstAirDate, tmdbId, _tmdb.GetPosterUrl(show.PosterPath), show.Overview);
    }

    /// <summary>
    /// Parses a search card's gap id.
    /// </summary>
    /// <param name="gapId">The id.</param>
    /// <param name="kind">The title kind.</param>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <returns><see langword="true"/> when the id is a movie or series recommendation id with a positive TMDB id.</returns>
    public static bool TryParseId(string? gapId, out BaseItemKind kind, out int tmdbId)
    {
        kind = default;
        tmdbId = 0;
        if (string.IsNullOrEmpty(gapId))
        {
            return false;
        }

        string prefix;
        if (gapId.StartsWith(GapSourceKeys.RecommendationMovie.GapPrefix, StringComparison.Ordinal))
        {
            kind = BaseItemKind.Movie;
            prefix = GapSourceKeys.RecommendationMovie.GapPrefix;
        }
        else if (gapId.StartsWith(GapSourceKeys.RecommendationSeries.GapPrefix, StringComparison.Ordinal))
        {
            kind = BaseItemKind.Series;
            prefix = GapSourceKeys.RecommendationSeries.GapPrefix;
        }
        else
        {
            return false;
        }

        return int.TryParse(gapId.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out tmdbId) && tmdbId > 0;
    }

    private static GapItem Gap(string id, BaseItemKind kind, MediaDomain domain, string? name, DateTime? released, int tmdbId, string? poster, string? overview)
        => new()
        {
            Id = id,
            Name = name ?? string.Empty,
            Year = released?.Year,
            ReleaseDate = released,
            IsUpcoming = released is null || released > DateTime.UtcNow,
            TargetKind = kind,
            Domain = domain,
            Pattern = GapPattern.Recommendation,
            SourceItemId = "search",
            SourceItemType = "Search",
            ImageUrl = poster,
            Overview = overview,
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = tmdbId.ToString(CultureInfo.InvariantCulture) }
        };

    private BaseItem? FindOwned(GapItem gap)
    {
        if (MissingTitleBuilder.TmdbId(gap) is not int tmdbId)
        {
            return null;
        }

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [gap.TargetKind],
            HasAnyProviderId = new Dictionary<string, string> { [ProviderIds.Tmdb] = tmdbId.ToString(CultureInfo.InvariantCulture) },
            Recursive = true,
            Limit = 1
        });
        return items.Count > 0 ? items[0] : null;
    }
}
