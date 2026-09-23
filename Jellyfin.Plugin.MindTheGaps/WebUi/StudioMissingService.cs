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
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TMDbLib.Objects.Search;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Answers "what has this studio made that I don't have?" for one library studio on demand, behind
/// jellyfin-web's own generic list page (routed by <c>studioId</c>). Resolves the studio's name to a TMDB
/// company by search (the same resolution <see cref="Configuration.PluginConfiguration.AutoSeedStudios"/>
/// uses for auto-seeding) and diffs the company's movies against the library, through the same
/// <see cref="CuratedSetGapMapper"/> the scan's own studio sets use, so a gap id here matches the report's
/// exactly when the scan already tracks the same studio. Movies only: see
/// <see cref="Configuration.PluginConfiguration.StudioPageEnabled"/> for why there is no TV-network
/// counterpart.
/// </summary>
public sealed class StudioMissingService
{
    // Matches CuratedSetGapSource's own studio cap, so a broad studio does not flood the row and the scan
    // and this on-demand lookup agree on how many gaps one studio can produce.
    private const int MaxPages = 10;
    private const int MaxTitles = 150;

    private readonly ILibraryManager _libraryManager;
    private readonly TmdbClient _tmdb;
    private readonly OwnershipIndexBuilder _ownershipIndexBuilder;
    private readonly ResolutionStore _resolutions;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StudioMissingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StudioMissingService"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="ownershipIndexBuilder">Indexes the owned movies.</param>
    /// <param name="resolutions">The dismissals, so a gap hidden on the report is hidden here too.</param>
    /// <param name="cache">The memory cache.</param>
    /// <param name="logger">The logger.</param>
    public StudioMissingService(
        ILibraryManager libraryManager,
        TmdbClient tmdb,
        OwnershipIndexBuilder ownershipIndexBuilder,
        ResolutionStore resolutions,
        IMemoryCache cache,
        ILogger<StudioMissingService> logger)
    {
        _libraryManager = libraryManager;
        _tmdb = tmdb;
        _ownershipIndexBuilder = ownershipIndexBuilder;
        _resolutions = resolutions;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Computes the unowned movies from an owned library studio.
    /// </summary>
    /// <param name="studioId">The Jellyfin studio item id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result, or <see langword="null"/> when the id is not a library studio.</returns>
    public async Task<StudioMissingResult?> GetAsync(Guid studioId, CancellationToken cancellationToken)
    {
        if (_libraryManager.GetItemById(studioId) is not BaseItem studio || studio.GetBaseItemKind() != BaseItemKind.Studio)
        {
            return null;
        }

        var result = new StudioMissingResult
        {
            StudioId = studioId,
            StudioName = studio.Name
        };

        var gaps = await BuildGapsAsync(studio.Name, cancellationToken).ConfigureAwait(false);
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
    /// Rehydrates one of the studio's missing movies by id, for a want-to-watch add or remove. Recomputed
    /// server-side from the same inputs the page listed.
    /// </summary>
    /// <param name="studioId">The Jellyfin studio item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The gap, or <see langword="null"/> when the studio or the gap is not there.</returns>
    public async Task<GapItem?> FindGapAsync(Guid studioId, string gapId, CancellationToken cancellationToken)
    {
        if (_libraryManager.GetItemById(studioId) is not BaseItem studio || studio.GetBaseItemKind() != BaseItemKind.Studio || string.IsNullOrEmpty(gapId))
        {
            return null;
        }

        var gaps = await BuildGapsAsync(studio.Name, cancellationToken).ConfigureAwait(false);
        return gaps.Gaps.FirstOrDefault(g => string.Equals(g.Id, gapId, StringComparison.Ordinal));
    }

    private async Task<(IReadOnlyList<GapItem> Gaps, string? Reason)> BuildGapsAsync(string studioName, CancellationToken cancellationToken)
    {
        (int Id, string Name)? company;
        try
        {
            company = await _tmdb.SearchCompanyAsync(studioName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Studio missing: search failed for '{Name}'", studioName);
            company = null;
        }

        if (company is null)
        {
            return ([], "This studio could not be matched to a TMDB company, so its movies cannot be looked up.");
        }

        var config = Plugin.RequireConfiguration();
        var kinds = new[] { BaseItemKind.Movie };
        var cacheKey = OwnershipCache.KeyFor(kinds);
        if (!_cache.TryGetValue(cacheKey, out OwnershipIndex? ownership) || ownership is null)
        {
            ownership = _ownershipIndexBuilder.Build(kinds);
            _cache.Set(cacheKey, ownership, OwnershipCache.Ttl);
        }

        var results = await CollectAsync(company.Value.Id, config.MetadataLanguage, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Studio missing: '{Name}' ({Id}) has {Count} movies on TMDB", company.Value.Name, company.Value.Id, results.Count);

        var gaps = CuratedSetGapMapper.BuildMovies(
            results,
            CuratedSetKeys.Company(company.Value.Id),
            company.Value.Name,
            SourceItemTypes.Studio,
            ownership,
            _tmdb.GetPosterUrl,
            MaxTitles).ToList();
        return (gaps, null);
    }

    // Pages through the company's discover results up to the page cap, matching CuratedSetGapSource's own
    // studio pagination so the two agree on how many titles a studio can surface.
    private async Task<List<SearchMovie>> CollectAsync(int companyId, string? language, CancellationToken cancellationToken)
    {
        var all = new List<SearchMovie>();
        var page = 1;
        var totalPages = 1;
        while (page <= totalPages && page <= MaxPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (pageResults, pages) = await _tmdb.DiscoverMoviesByCompanyAsync(companyId, page, language, cancellationToken).ConfigureAwait(false);
            if (pageResults.Count == 0)
            {
                break;
            }

            all.AddRange(pageResults);
            totalPages = pages;
            page++;
        }

        return all;
    }
}
