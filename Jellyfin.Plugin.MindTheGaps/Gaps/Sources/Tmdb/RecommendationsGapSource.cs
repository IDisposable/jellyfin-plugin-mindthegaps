using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Discovery source: surfaces TMDB "similar" movies/series for owned titles that aren't in the library.
/// Opt-in (off by default) since it can produce a lot of suggestions.
/// </summary>
public sealed class RecommendationsGapSource : IGapSource
{
    private readonly ILibraryManager _libraryManager;
    private readonly TmdbClient _tmdb;
    private readonly ScanCursorStore _cursors;
    private readonly ResolutionStore _resolutions;
    private readonly ILogger<RecommendationsGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationsGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="cursors">Tracks which seed titles were scanned, for cross-run staleness rotation.</param>
    /// <param name="resolutions">Holds dismissals, including dismissed recommendation sources to skip.</param>
    /// <param name="logger">The logger.</param>
    public RecommendationsGapSource(
        ILibraryManager libraryManager,
        TmdbClient tmdb,
        ScanCursorStore cursors,
        ResolutionStore resolutions,
        ILogger<RecommendationsGapSource> logger)
    {
        _libraryManager = libraryManager;
        _tmdb = tmdb;
        _cursors = cursors;
        _resolutions = resolutions;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Recommendations";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie, BaseItemKind.Series };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.ScanRecommendations;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var language = context.Config.MetadataLanguage;
        var perItem = Math.Max(1, context.Config.MaxRelatedPerItem);

        var ownedMovies = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie },
            Recursive = true
        });
        var ownedSeries = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Series },
            Recursive = true
        });

        var dismissed = DismissedRecSourceGuids();
        var lastScanned = _cursors.GetLastScanned(Name);

        // Build one combined seed pool of owned movies and series, then take the stalest MaxRecommendationSeeds
        // (never-scanned first), so over repeated runs every owned title gets used as a recommendation seed and
        // then the seeds scanned longest ago refresh. The engine carries unowned recommendation gaps forward
        // between runs, so coverage accumulates rather than churning each scan.
        var seeds = ownedMovies.Select(m => (Item: m, IsMovie: true))
            .Concat(ownedSeries.Select(s => (Item: s, IsMovie: false)))
            .Select(x => (x.Item, x.IsMovie, Key: x.Item.Id.ToString("N", CultureInfo.InvariantCulture)))
            .Where(x => !dismissed.Contains(x.Key))
            .OrderBy(x => lastScanned.TryGetValue(x.Key, out var t) ? t : DateTime.MinValue)
            .ThenBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(GapScanLimits.MaxRecommendationSeeds)
            .ToList();

        var scannedKeys = new List<string>(seeds.Count);
        for (var index = 0; index < seeds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index / Math.Max(1, seeds.Count));

            var (item, isMovie, key) = seeds[index];
            scannedKeys.Add(key);

            if (!item.TryGetProviderId(MetadataProvider.Tmdb, out var idStr)
                || !int.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
            {
                continue;
            }

            if (isMovie)
            {
                (IReadOnlyList<TMDbLib.Objects.Search.SearchMovie> Results, int TotalPages) similar;
                try
                {
                    similar = await _tmdb.GetMovieSimilarPageAsync(tmdbId, 1, language, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Recommendations: failed to fetch similar movies for {Id}", tmdbId);
                    continue;
                }

                foreach (var gap in RecommendationGapMapper.BuildMovies(
                    similar.Results,
                    key,
                    item.Name,
                    item.ProductionYear,
                    context.Ownership,
                    _tmdb.GetPosterUrl,
                    perItem))
                {
                    yield return gap;
                }
            }
            else
            {
                (IReadOnlyList<TMDbLib.Objects.Search.SearchTv> Results, int TotalPages) similar;
                try
                {
                    similar = await _tmdb.GetSeriesSimilarPageAsync(tmdbId, 1, language, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Recommendations: failed to fetch similar series for {Id}", tmdbId);
                    continue;
                }

                foreach (var gap in RecommendationGapMapper.BuildSeries(
                    similar.Results,
                    key,
                    item.Name,
                    item.ProductionYear,
                    context.Ownership,
                    _tmdb.GetPosterUrl,
                    perItem))
                {
                    yield return gap;
                }
            }
        }

        _cursors.MarkScanned(Name, scannedKeys);
    }

    // The set of owned-item guids (N-format) the user dismissed as a recommendation source.
    private HashSet<string> DismissedRecSourceGuids()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in _resolutions.GetAll().Keys)
        {
            if (id.StartsWith(GapResolution.RecSourcePrefix, StringComparison.Ordinal))
            {
                set.Add(id[GapResolution.RecSourcePrefix.Length..]);
            }
        }

        return set;
    }
}
