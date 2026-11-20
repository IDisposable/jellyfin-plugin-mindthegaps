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
using Jellyfin.Plugin.MindTheGaps.Services.Imdb;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Imdb;

/// <summary>
/// The shared walk over the title lists in <see cref="PluginConfiguration.ImdbListIds"/>. For each id it
/// surfaces the titles the library does not own as <see cref="GapPattern.Recommendation"/> gaps, keyed by the
/// IMDb id every entry carries. Opt-in and keyless: it needs a Discover toggle and at least one id, and reads
/// only what the IMDb account has published (a private list answers "permission denied", and is skipped with a
/// warning). A list and a watchlist are separate sources, since only a list may show to other users.
/// </summary>
internal abstract class ImdbTitleListGapSourceBase : IGapSource, IDiscoverSource, IConfiguredScopeSource
{
    // A watchlist is a want-list rather than a feed, so it is capped well above the 200 a community list gets:
    // truncating what someone deliberately marked is worse than a long tab.
    private const int MaxGapsPerList = 1000;

    private readonly ImdbClient _imdb;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImdbTitleListGapSourceBase"/> class.
    /// </summary>
    /// <param name="imdb">The IMDb client.</param>
    /// <param name="logger">The logger.</param>
    protected ImdbTitleListGapSourceBase(ImdbClient imdb, ILogger logger)
    {
        _imdb = imdb;
        _logger = logger;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string DiscoverKind { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie, BaseItemKind.Series };

    /// <inheritdoc />
    public string GapIdPrefix => GapSourceKeys.ImdbList.GapPrefix;

    /// <summary>
    /// Gets a value indicating whether this source reads watchlists rather than lists.
    /// </summary>
    protected abstract bool ReadsWatchlists { get; }

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanImdbLists && ImdbListInput.ParseIds(config.ImdbListIds).Count > 0;

    /// <inheritdoc />
    public bool StillInScope(GapItem item, PluginConfiguration config)
        => config.ScanImdbLists
            && ImdbListInput.ParseIds(config.ImdbListIds).Any(id => string.Equals(GapSourceKeys.ImdbList.Owner(id), item.SourceItemId, StringComparison.Ordinal));

    /// <inheritdoc />
    public IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return FindGapsForListsAsync(context, ImdbListInput.ParseIds(context.Config.ImdbListIds), cancellationToken);
    }

    /// <summary>
    /// Streams the gaps for an explicit set of IMDb ids, diffed against the context's ownership index.
    /// </summary>
    /// <param name="context">The scan context.</param>
    /// <param name="ids">The IMDb list ids ("ls...") or user ids ("ur...") to read and diff.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async stream of gaps.</returns>
    public async IAsyncEnumerable<GapItem> FindGapsForListsAsync(
        GapScanContext context,
        IReadOnlyList<string> ids,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ids);

        var total = Math.Max(1, ids.Count);
        var done = 0;

        // An id that fails to read may be of either class, so it counts as read for both sources.
        var readOwn = false;

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var byUser = id.StartsWith("ur", StringComparison.Ordinal);
            if (byUser && !ReadsWatchlists)
            {
                context.ReportProgress((double)++done / total);
                continue;
            }

            if (ServiceCircuit.IsOpen(ServiceNames.Imdb))
            {
                _logger.LogWarning("IMDb: service unavailable this run; skipping the remaining lists");
                readOwn = true;
                break;
            }

            ImdbListContents? list;
            try
            {
                list = await _imdb.GetListAsync(id, MaxGapsPerList, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "IMDb: failed to read {Id}", id);
                readOwn = true;
                context.ReportProgress((double)++done / total);
                continue;
            }

            if (list is null)
            {
                _logger.LogWarning("IMDb: {Id} could not be read; it may be private or may not exist", id);
                readOwn = true;
                context.ReportProgress((double)++done / total);
                continue;
            }

            // A people list is the people source's input, and the other title source reads the other class. A
            // user id is always read as that user's watchlist.
            if (string.Equals(list.ListType, ImdbListContents.PeopleType, StringComparison.Ordinal)
                || (byUser || list.IsWatchlist) != ReadsWatchlists)
            {
                context.ReportProgress((double)++done / total);
                continue;
            }

            readOwn = true;
            var titles = list.Titles.ToList();
            var name = list.Name ?? string.Create(CultureInfo.InvariantCulture, $"IMDb {list.Id}");
            _logger.LogInformation("IMDb: list '{Name}' ({Id}) has {Count} titles", name, list.Id, titles.Count);

            foreach (var gap in ImdbListMapper.Build(list.Id, id, name, titles, context.Ownership, MaxGapsPerList, DiscoverKind))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / total);
        }

        if (!readOwn)
        {
            context.ReportNothingRead();
        }
    }
}
