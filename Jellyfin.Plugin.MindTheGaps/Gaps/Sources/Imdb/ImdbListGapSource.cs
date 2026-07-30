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
/// Discovery source over IMDb watchlists and lists. For each configured id it surfaces the titles the library
/// does not own as <see cref="GapPattern.Recommendation"/> gaps, keyed by the IMDb id every entry carries.
/// Opt-in and keyless: it needs a Discover toggle and at least one id, and reads only what the IMDb account
/// has published (a private list answers "permission denied", and is skipped with a warning).
/// </summary>
/// <remarks>
/// There is no explore chip for this source. An explore id is an <see cref="int"/>, and an IMDb id is a
/// zero-padded string ("ls055576446"), so a round trip through an int would quietly address a different list.
/// </remarks>
internal sealed class ImdbListGapSource : IGapSource
{
    // A watchlist is a want-list rather than a feed, so it is capped well above the 200 a community list gets:
    // truncating what someone deliberately marked is worse than a long tab.
    private const int MaxGapsPerList = 1000;

    private readonly ImdbClient _imdb;
    private readonly ILogger<ImdbListGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImdbListGapSource"/> class.
    /// </summary>
    /// <param name="imdb">The IMDb client.</param>
    /// <param name="logger">The logger.</param>
    public ImdbListGapSource(ImdbClient imdb, ILogger<ImdbListGapSource> logger)
    {
        _imdb = imdb;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "IMDb watchlists";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie, BaseItemKind.Series };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanImdbLists && ImdbListInput.ParseIds(config.ImdbListIds).Count > 0;

    /// <inheritdoc />
    public IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        CancellationToken cancellationToken)
        => FindGapsForListsAsync(context, ImdbListInput.ParseIds(context.Config.ImdbListIds), cancellationToken);

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

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ServiceCircuit.IsOpen(ServiceNames.Imdb))
            {
                _logger.LogWarning("IMDb: service unavailable this run; skipping the remaining lists");
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
                context.ReportProgress((double)++done / total);
                continue;
            }

            if (list is null)
            {
                _logger.LogWarning("IMDb: {Id} could not be read; it may be private or may not exist", id);
                context.ReportProgress((double)++done / total);
                continue;
            }

            // A people list is the other source's input; reading it here would emit nothing and log a zero.
            if (string.Equals(list.ListType, ImdbListContents.PeopleType, StringComparison.Ordinal))
            {
                context.ReportProgress((double)++done / total);
                continue;
            }

            var titles = list.Titles.ToList();
            var name = list.Name ?? string.Create(CultureInfo.InvariantCulture, $"IMDb {list.Id}");
            _logger.LogInformation("IMDb: list '{Name}' ({Id}) has {Count} titles", name, list.Id, titles.Count);

            foreach (var gap in ImdbListMapper.Build(list.Id, name, titles, context.Ownership, MaxGapsPerList))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / total);
        }
    }
}
