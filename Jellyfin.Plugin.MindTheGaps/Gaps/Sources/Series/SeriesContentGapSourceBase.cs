using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Shared logic for the series-completeness cross-checks. For each owned series it asks an
/// external source (TVmaze, TheTVDB) for the canonical episode list and reports the regular
/// episodes the library doesn't have on disk. Gap ids match the library reader, so duplicates from
/// different sources collapse to one todo entry.
/// </summary>
public abstract class SeriesContentGapSourceBase : IGapSource
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesContentGapSourceBase"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    protected SeriesContentGapSourceBase(ILibraryManager libraryManager, ILogger logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    // Reads the library directly per series, so it needs nothing in the ownership index.
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = Array.Empty<BaseItemKind>();

    /// <summary>
    /// Gets the maximum number of series to hit the external API for in a single run.
    /// </summary>
    protected abstract int MaxSeries { get; }

    /// <inheritdoc />
    public abstract bool IsEnabled(PluginConfiguration config);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var allSeries = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Series },
            Recursive = true
        });

        // 0 in config means "no limit".
        var episodeCap = context.Config.MaxMissingEpisodesPerShow;
        if (episodeCap <= 0)
        {
            episodeCap = int.MaxValue;
        }

        var processed = 0;
        var index = 0;
        foreach (var series in allSeries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index++ / Math.Max(1, allSeries.Count));

            // Series without an id this source can look up cost no API calls and don't use budget.
            if (!HasLookupId(series))
            {
                continue;
            }

            if (processed >= MaxSeries)
            {
                _logger.LogInformation("{Source}: reached series cap ({Cap}); remaining not scanned this run", Name, MaxSeries);
                break;
            }

            processed++;

            var canonical = await GetCanonicalEpisodesAsync(series, context, cancellationToken).ConfigureAwait(false);
            if (canonical is null || canonical.Count == 0)
            {
                continue;
            }

            var owned = GetOwnedEpisodeNumbers(series.Id);
            foreach (var episode in SeriesContentDiff.Missing(canonical, owned, episodeCap))
            {
                yield return BuildGap(series, episode);
            }
        }
    }

    /// <summary>
    /// Determines whether the series carries an id this source can resolve, without any network call.
    /// </summary>
    /// <param name="series">The owned series.</param>
    /// <returns><see langword="true"/> if this source can attempt a lookup.</returns>
    protected abstract bool HasLookupId(BaseItem series);

    /// <summary>
    /// Fetches the canonical episode list for an owned series from the external source.
    /// </summary>
    /// <param name="series">The owned series.</param>
    /// <param name="context">The scan context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The canonical episodes, or <see langword="null"/> if the series could not be resolved.</returns>
    protected abstract Task<IReadOnlyList<CanonicalEpisode>?> GetCanonicalEpisodesAsync(
        BaseItem series,
        GapScanContext context,
        CancellationToken cancellationToken);

    private static GapItem BuildGap(BaseItem series, CanonicalEpisode episode)
    {
        var code = string.Create(CultureInfo.InvariantCulture, $"S{episode.Season:D2}E{episode.Number:D2}");
        var name = string.IsNullOrEmpty(episode.Name)
            ? string.Create(CultureInfo.InvariantCulture, $"{series.Name} {code}")
            : string.Create(CultureInfo.InvariantCulture, $"{series.Name} {code} - {episode.Name}");

        return GapItemFactory.Create(
            id: SeriesGapKey.Episode(series.Id, episode.Season, episode.Number),
            pattern: GapPattern.SetCompletion,
            domain: MediaDomain.Shows,
            targetKind: BaseItemKind.Episode,
            name: name,
            providerIds: new Dictionary<string, string>(),
            sourceItemId: series.Id.ToString("N", CultureInfo.InvariantCulture),
            sourceItemName: series.Name,
            sourceItemType: "Series",
            releaseDate: episode.ReleaseDate,
            overview: episode.Overview,
            season: episode.Season,
            sourceItemYear: series.ProductionYear);
    }

    private HashSet<(int Season, int Number)> GetOwnedEpisodeNumbers(Guid seriesId)
    {
        var owned = new HashSet<(int Season, int Number)>();
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = new[] { seriesId },
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            if (item is Episode episode
                && episode.ParentIndexNumber is int season
                && episode.IndexNumber is int number)
            {
                owned.Add((season, number));
            }
        }

        return owned;
    }
}
