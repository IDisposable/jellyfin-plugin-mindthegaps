using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.ListenBrainz;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Music;

/// <summary>
/// Discovery source: the artists ListenBrainz finds similar to each owned artist that the library does not
/// own. Seeds are owned artists with a MusicBrainz id, a slice per run, stalest first, as the TMDB
/// recommendations rotate; the engine carries the earlier slices' gaps forward. Opt-in.
/// </summary>
internal sealed class MusicRecommendationsGapSource : IGapSource, IDiscoverSource
{
    private const int MaxSeedsPerRun = 100;

    private readonly ILibraryManager _libraryManager;
    private readonly ListenBrainzClient _listenBrainz;
    private readonly ScanCursorStore _cursors;
    private readonly ResolutionStore _resolutions;
    private readonly ILogger<MusicRecommendationsGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicRecommendationsGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="listenBrainz">The ListenBrainz client.</param>
    /// <param name="cursors">Tracks which seed artists were scanned, for the rotation.</param>
    /// <param name="resolutions">Holds dismissals, including dismissed recommendation sources to skip.</param>
    /// <param name="logger">The logger.</param>
    public MusicRecommendationsGapSource(
        ILibraryManager libraryManager,
        ListenBrainzClient listenBrainz,
        ScanCursorStore cursors,
        ResolutionStore resolutions,
        ILogger<MusicRecommendationsGapSource> logger)
    {
        _libraryManager = libraryManager;
        _listenBrainz = listenBrainz;
        _cursors = cursors;
        _resolutions = resolutions;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Music recommendations";

    /// <inheritdoc />
    public string DiscoverKind => SourceItemTypes.MusicArtist;

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.MusicArtist };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config) => config.ScanMusicRecommendations;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var perArtist = Math.Max(1, context.Config.MaxRelatedPerItem);

        var pool = _libraryManager.GetItemList(new InternalItemsQuery
            {
                DtoOptions = LibraryQueryOptions.WithProviderIds(),
                IncludeItemTypes = new[] { BaseItemKind.MusicArtist },
                Recursive = true
            })
            .Select(a => (Item: a, Mbid: a.GetProviderId(ProviderIds.MusicBrainzArtist), Key: a.Id.ToString("N", CultureInfo.InvariantCulture)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Mbid))
            .ToList();

        _cursors.RetainOnly(Name, pool.Select(x => x.Key).ToHashSet(StringComparer.Ordinal));

        var dismissed = DismissedRecSourceGuids();
        var lastScanned = _cursors.GetLastScanned(Name);
        var seeds = pool
            .Where(x => !dismissed.Contains(x.Key))
            .OrderByStalest(lastScanned, x => x.Key)
            .ThenBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSeedsPerRun)
            .ToList();

        var scannedKeys = new List<string>(seeds.Count);
        for (var index = 0; index < seeds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index / Math.Max(1, seeds.Count));
            if (ServiceCircuit.IsOpen(ServiceNames.ListenBrainz))
            {
                _logger.LogWarning("Music recommendations: ListenBrainz is unavailable this run; skipping the remaining artists");
                break;
            }

            var (item, mbid, key) = seeds[index];
            List<ListenBrainzSimilarArtist>? similar;
            try
            {
                similar = await _listenBrainz.GetSimilarArtistsAsync(mbid!, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Music recommendations: failed to fetch similar artists for {Artist}", item.Name);
                continue;
            }

            scannedKeys.Add(key);
            if (similar is null)
            {
                continue;
            }

            foreach (var gap in MusicRecommendationMapper.Build(similar, key, item.Name, mbid!, context.Ownership, perArtist))
            {
                yield return gap;
            }
        }

        _cursors.MarkScanned(Name, scannedKeys);
    }

    private HashSet<string> DismissedRecSourceGuids()
        => _resolutions.GetAll().Keys
            .Where(id => id.StartsWith(GapResolution.RecSourcePrefix, StringComparison.Ordinal))
            .Select(id => id[GapResolution.RecSourcePrefix.Length..])
            .ToHashSet(StringComparer.Ordinal);
}
