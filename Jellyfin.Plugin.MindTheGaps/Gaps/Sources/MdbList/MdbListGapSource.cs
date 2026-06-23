using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.MdbList;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.MdbList;

/// <summary>
/// Discovery source over MDBList's community lists (https://mdblist.com/toplists). For each configured list
/// it surfaces the titles the library does not own as <see cref="GapPattern.Recommendation"/> gaps, keyed
/// by the TMDB/IMDb ids the list already carries. Opt-in: needs a Discover toggle, an MDBList API key, and
/// at least one chosen list.
/// </summary>
public sealed class MdbListGapSource : IGapSource
{
    // Cap a single list so a huge community list does not flood the discovery feed.
    private const int MaxGapsPerList = 200;

    private readonly MdbListClient _mdblist;
    private readonly ILogger<MdbListGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MdbListGapSource"/> class.
    /// </summary>
    /// <param name="mdblist">The MDBList client.</param>
    /// <param name="logger">The logger.</param>
    public MdbListGapSource(MdbListClient mdblist, ILogger<MdbListGapSource> logger)
    {
        _mdblist = mdblist;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "MDBList lists";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie, BaseItemKind.Series };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanMdbList
            && !string.IsNullOrWhiteSpace(config.MdbListApiKey)
            && ParseIds(config.MdbListListIds).Count > 0;

    /// <inheritdoc />
    public IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        CancellationToken cancellationToken)
        => FindGapsForListsAsync(context, ParseIds(context.Config.MdbListListIds), cancellationToken);

    /// <summary>
    /// Streams the gaps for an explicit set of list ids, diffed against the context's ownership index. The
    /// scan path calls this with the configured ids; an ad-hoc "explore a source" run calls it with one id
    /// the user picked, so a single list can be surfaced without a full rescan.
    /// </summary>
    /// <param name="context">The scan context.</param>
    /// <param name="listIds">The MDBList list ids to fetch and diff.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async stream of gaps.</returns>
    public async IAsyncEnumerable<GapItem> FindGapsForListsAsync(
        GapScanContext context,
        IReadOnlyList<int> listIds,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(listIds);

        var total = Math.Max(1, listIds.Count);
        var done = 0;

        foreach (var listId in listIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ServiceCircuit.IsOpen(ServiceNames.MdbList))
            {
                _logger.LogInformation("MDBList: service unavailable this run; skipping the remaining lists");
                break;
            }

            IReadOnlyList<MdbListItem> items;
            try
            {
                items = await _mdblist.GetListItemsAsync(listId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "MDBList: failed to fetch items for list {ListId}", listId);
                context.ReportProgress((double)++done / total);
                continue;
            }

            var name = await _mdblist.GetListNameAsync(listId, cancellationToken).ConfigureAwait(false)
                ?? string.Create(CultureInfo.InvariantCulture, $"MDBList {listId}");
            _logger.LogInformation("MDBList: list '{Name}' ({Id}) has {Count} items", name, listId, items.Count);

            foreach (var gap in MdbListMapper.Build(listId, name, items, context.Ownership, MaxGapsPerList))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / total);
        }
    }

    private static IReadOnlyList<int> ParseIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<int>();
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }
}
