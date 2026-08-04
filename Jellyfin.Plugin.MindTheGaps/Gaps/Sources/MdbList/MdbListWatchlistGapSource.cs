using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.MdbList;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.MdbList;

/// <summary>
/// Discovery source over the MDBList account's own watchlist: the movies and shows marked as wanted that the
/// library does not hold. Opt-in, and it needs only the MDBList API key the community-list source already
/// uses, because MDBList serves the key's own watchlist.
/// </summary>
internal sealed class MdbListWatchlistGapSource : IGapSource, IDiscoverSource
{
    // A watchlist is a deliberate list, so it is capped far above the 200 a community list gets.
    private const int MaxGaps = 1000;

    private readonly MdbListClient _mdblist;
    private readonly ILogger<MdbListWatchlistGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MdbListWatchlistGapSource"/> class.
    /// </summary>
    /// <param name="mdblist">The MDBList client.</param>
    /// <param name="logger">The logger.</param>
    public MdbListWatchlistGapSource(MdbListClient mdblist, ILogger<MdbListWatchlistGapSource> logger)
    {
        _mdblist = mdblist;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "MDBList watchlist";

    /// <inheritdoc />
    public string DiscoverKind => SourceItemTypes.MdbListWatchlist;

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie, BaseItemKind.Series };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanMdbListWatchlist && !string.IsNullOrWhiteSpace(config.MdbListApiKey);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (ServiceCircuit.IsOpen(ServiceNames.MdbList))
        {
            _logger.LogWarning("MDBList watchlist: service unavailable this run");
            yield break;
        }

        IReadOnlyList<MdbListItem>? items;
        try
        {
            items = await _mdblist.GetWatchlistAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MDBList watchlist: failed to read the watchlist");
            yield break;
        }

        if (items is null)
        {
            _logger.LogWarning("MDBList watchlist: could not be read; the API key may be wrong or expired");
            yield break;
        }

        _logger.LogInformation("MDBList watchlist: {Count} titles", items.Count);
        context.ReportProgress(0.5);

        foreach (var gap in MdbListMapper.BuildWatchlist(items, context.Ownership, MaxGaps))
        {
            yield return gap;
        }

        context.ReportProgress(1);
    }
}
