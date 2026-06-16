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

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Library;

/// <summary>
/// Surfaces missing series content into the todo list by reading the virtual (missing) episodes
/// that Jellyfin core already mints for owned series.
/// </summary>
public sealed class SeriesContentGapSource : IGapSource
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<SeriesContentGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesContentGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public SeriesContentGapSource(ILibraryManager libraryManager, ILogger<SeriesContentGapSource> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Series content";

    /// <inheritdoc />
    // Reads the library directly (missing episodes), so it needs nothing in the ownership index.
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = Array.Empty<BaseItemKind>();

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.ScanSeries;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Async iterator over a synchronous source; yield to keep the signature uniform.
        await Task.CompletedTask.ConfigureAwait(false);

        // One query for every missing episode in the library, then group/cap by series in memory.
        // This avoids a per-series query storm on large libraries.
        var missing = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            IsMissing = true,
            Recursive = true
        });

        // 0 in config means "no limit".
        var cap = context.Config.MaxMissingEpisodesPerShow;
        if (cap <= 0)
        {
            cap = int.MaxValue;
        }

        var perSeriesCount = new Dictionary<Guid, int>();
        var cappedSeries = new HashSet<Guid>();

        foreach (var item in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item is not Episode episode)
            {
                continue;
            }

            var seriesId = episode.SeriesId;
            perSeriesCount.TryGetValue(seriesId, out var count);
            if (count >= cap)
            {
                if (cappedSeries.Add(seriesId))
                {
                    _logger.LogInformation(
                        "Series content: {Series} has more than {Cap} missing episodes; truncated",
                        episode.SeriesName,
                        cap);
                }

                continue;
            }

            perSeriesCount[seriesId] = count + 1;
            yield return BuildGap(episode);
        }
    }

    private static GapItem BuildGap(Episode episode)
    {
        var season = episode.ParentIndexNumber;
        var number = episode.IndexNumber;
        string? code = null;
        if (season.HasValue && number.HasValue)
        {
            var end = episode.IndexNumberEnd;
            code = end.HasValue && end.Value > number.Value
                ? string.Create(CultureInfo.InvariantCulture, $"S{season.Value:D2}E{number.Value:D2}-E{end.Value:D2}")
                : string.Create(CultureInfo.InvariantCulture, $"S{season.Value:D2}E{number.Value:D2}");
        }

        var name = code is null
            ? string.Create(CultureInfo.InvariantCulture, $"{episode.SeriesName} - {episode.Name}")
            : string.Create(CultureInfo.InvariantCulture, $"{episode.SeriesName} {code} - {episode.Name}");

        var providerIds = new Dictionary<string, string>(episode.ProviderIds, StringComparer.OrdinalIgnoreCase);

        // Share the episode key with the TVmaze/TheTVDB cross-checks so the same missing episode
        // surfaced by more than one source collapses to a single todo entry.
        var id = season.HasValue && number.HasValue
            ? SeriesGapKey.Episode(episode.SeriesId, season.Value, number.Value)
            : string.Create(CultureInfo.InvariantCulture, $"seriescontent:{episode.Id:N}");

        return GapItemFactory.Create(
            id: id,
            pattern: GapPattern.SetCompletion,
            domain: MediaDomain.Video,
            targetKind: BaseItemKind.Episode,
            name: name,
            providerIds: providerIds,
            sourceItemId: episode.SeriesId.ToString("N", CultureInfo.InvariantCulture),
            sourceItemName: episode.SeriesName,
            sourceItemType: "Series",
            releaseDate: episode.PremiereDate,
            overview: episode.Overview);
    }
}
