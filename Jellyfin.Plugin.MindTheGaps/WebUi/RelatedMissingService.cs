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
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Answers "what is like this title that I don't have?" for one owned movie or series on demand: TMDB's
/// recommendations for the item, through the shared TMDB client, run through the same recommendation
/// mapper and ownership index the scan uses, minus what the report has dismissed. Independent of the scan
/// rotation, so a title the scan has not used as a seed yet still gets an answer, and identical to what the
/// scan would have produced for it.
/// </summary>
public sealed class RelatedMissingService
{
    private static readonly BaseItemKind[] _kinds = [BaseItemKind.Movie, BaseItemKind.Series];

    private readonly ILibraryManager _libraryManager;
    private readonly TmdbClient _tmdb;
    private readonly OwnershipIndexBuilder _ownershipIndexBuilder;
    private readonly ResolutionStore _resolutions;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RelatedMissingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RelatedMissingService"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="ownershipIndexBuilder">Indexes the owned movies and series.</param>
    /// <param name="resolutions">The dismissals, so a gap hidden on the report is hidden here too.</param>
    /// <param name="cache">The memory cache.</param>
    /// <param name="logger">The logger.</param>
    public RelatedMissingService(
        ILibraryManager libraryManager,
        TmdbClient tmdb,
        OwnershipIndexBuilder ownershipIndexBuilder,
        ResolutionStore resolutions,
        IMemoryCache cache,
        ILogger<RelatedMissingService> logger)
    {
        _libraryManager = libraryManager;
        _tmdb = tmdb;
        _ownershipIndexBuilder = ownershipIndexBuilder;
        _resolutions = resolutions;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Computes the unowned titles similar to an owned movie or series.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="isAdministrator">Whether the caller is an administrator, which gates the Send buttons.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result, or <see langword="null"/> when the id is not a library movie or series.</returns>
    public async Task<RelatedMissingResult?> GetAsync(Guid itemId, bool isAdministrator, CancellationToken cancellationToken)
    {
        if (_libraryManager.GetItemById(itemId) is not BaseItem item || item is not (Movie or Series))
        {
            return null;
        }

        var config = Plugin.RequireConfiguration();
        var result = new RelatedMissingResult
        {
            ItemId = itemId,
            ItemName = item.Name,
            CanSend = isAdministrator && (item is Movie ? AcquisitionService.RadarrConfigured(config) : AcquisitionService.SonarrConfigured(config))
        };

        var gaps = await BuildGapsAsync(item, cancellationToken).ConfigureAwait(false);
        if (gaps.Reason is not null)
        {
            result.Reason = gaps.Reason;
            return result;
        }

        var resolutions = _resolutions.GetAll();
        var titles = new List<MissingTitle>();
        foreach (var gap in gaps.Gaps)
        {
            if (resolutions.ContainsKey(gap.Id))
            {
                continue;
            }

            var title = MissingTitleBuilder.ToTitle(gap, null, null);
            if (title is not null)
            {
                titles.Add(title);
            }
        }

        result.Titles = titles;
        return result;
    }

    /// <summary>
    /// Rehydrates one of the item's related gaps by id, for a Send. Recomputed server-side from the same
    /// inputs the page listed.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The gap, or <see langword="null"/> when the item or the gap is not there.</returns>
    public async Task<GapItem?> FindGapAsync(Guid itemId, string gapId, CancellationToken cancellationToken)
    {
        if (_libraryManager.GetItemById(itemId) is not BaseItem item || item is not (Movie or Series) || string.IsNullOrEmpty(gapId))
        {
            return null;
        }

        var gaps = await BuildGapsAsync(item, cancellationToken).ConfigureAwait(false);
        return gaps.Gaps.FirstOrDefault(g => string.Equals(g.Id, gapId, StringComparison.Ordinal));
    }

    private async Task<(IReadOnlyList<GapItem> Gaps, string? Reason)> BuildGapsAsync(BaseItem item, CancellationToken cancellationToken)
    {
        if (!item.TryGetProviderIdAsInt(ProviderIds.Tmdb, out var tmdbId))
        {
            return ([], "This title has no TMDB id in the library, so similar titles cannot be looked up.");
        }

        var config = Plugin.RequireConfiguration();
        if (!_cache.TryGetValue(OwnershipCache.Key, out OwnershipIndex? ownership) || ownership is null)
        {
            ownership = _ownershipIndexBuilder.Build(_kinds);
            _cache.Set(OwnershipCache.Key, ownership, OwnershipCache.Ttl);
        }

        var sourceId = item.Id.ToString("N", CultureInfo.InvariantCulture);
        var perItem = Math.Max(1, config.MaxRelatedPerItem);
        if (item is Movie)
        {
            var results = await _tmdb.GetMovieRecommendationsAsync(tmdbId, config.MetadataLanguage, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Related: {Count} recommended movies for '{Name}'", results.Count, item.Name);
            return (RecommendationGapMapper.BuildMovies(results, sourceId, item.Name, item.ProductionYear, ownership, _tmdb.GetPosterUrl, perItem, config.MinRecommendationVotes).ToList(), null);
        }

        var series = await _tmdb.GetSeriesRecommendationsAsync(tmdbId, config.MetadataLanguage, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Related: {Count} recommended series for '{Name}'", series.Count, item.Name);
        return (RecommendationGapMapper.BuildSeries(series, sourceId, item.Name, item.ProductionYear, ownership, _tmdb.GetPosterUrl, perItem, config.MinRecommendationVotes).ToList(), null);
    }
}
