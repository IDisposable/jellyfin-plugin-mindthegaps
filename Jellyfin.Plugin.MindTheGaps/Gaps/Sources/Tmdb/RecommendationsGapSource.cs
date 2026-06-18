using System;
using System.Collections.Generic;
using System.Globalization;
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
    private readonly ResolutionStore _resolutions;
    private readonly ILogger<RecommendationsGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationsGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="resolutions">Holds dismissals, including dismissed recommendation sources to skip.</param>
    /// <param name="logger">The logger.</param>
    public RecommendationsGapSource(
        ILibraryManager libraryManager,
        TmdbClient tmdb,
        ResolutionStore resolutions,
        ILogger<RecommendationsGapSource> logger)
    {
        _libraryManager = libraryManager;
        _tmdb = tmdb;
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
        var totalSeeds = Math.Max(1, ownedMovies.Count + ownedSeries.Count);
        var done = 0;
        var dismissed = DismissedRecSourceGuids();

        // Movie recommendations.
        var seedMovies = 0;
        foreach (var movie in ownedMovies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)done++ / totalSeeds);
            if (dismissed.Contains(movie.Id.ToString("N", CultureInfo.InvariantCulture)))
            {
                continue;
            }

            if (seedMovies >= GapScanLimits.MaxRecommendationSeeds)
            {
                break;
            }

            if (!movie.TryGetProviderId(MetadataProvider.Tmdb, out var idStr)
                || !int.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
            {
                continue;
            }

            seedMovies++;

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
                movie.Id.ToString("N", CultureInfo.InvariantCulture),
                movie.Name,
                movie.ProductionYear,
                context.Ownership,
                _tmdb.GetPosterUrl,
                perItem))
            {
                yield return gap;
            }
        }

        // Series recommendations.
        var seedSeries = 0;
        foreach (var series in ownedSeries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)done++ / totalSeeds);
            if (dismissed.Contains(series.Id.ToString("N", CultureInfo.InvariantCulture)))
            {
                continue;
            }

            if (seedSeries >= GapScanLimits.MaxRecommendationSeeds)
            {
                break;
            }

            if (!series.TryGetProviderId(MetadataProvider.Tmdb, out var idStr)
                || !int.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
            {
                continue;
            }

            seedSeries++;

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
                series.Id.ToString("N", CultureInfo.InvariantCulture),
                series.Name,
                series.ProductionYear,
                context.Ownership,
                _tmdb.GetPosterUrl,
                perItem))
            {
                yield return gap;
            }
        }
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
