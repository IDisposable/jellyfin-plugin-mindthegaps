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
using Jellyfin.Plugin.MindTheGaps.Services.Trakt;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Trakt;

/// <summary>
/// Discovery source over Trakt lists (the Trakt sibling of the MDBList source). For each configured list it
/// surfaces the titles the library does not own as <see cref="GapPattern.Recommendation"/> gaps, keyed by
/// the TMDB/IMDb ids the list already carries. Opt-in: needs a Discover toggle, a Trakt client id, and at
/// least one chosen list. A Trakt list can hold both movies and shows.
/// </summary>
internal sealed class TraktListGapSource : IGapSource, IExploreSource
{
    // Cap a single list so a huge list does not flood the discovery feed.
    private const int MaxGapsPerList = 200;

    private readonly TraktClient _trakt;
    private readonly ILogger<TraktListGapSource> _logger;
    private readonly IReadOnlyCollection<ExploreDescriptor> _exploreDescriptors;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraktListGapSource"/> class.
    /// </summary>
    /// <param name="trakt">The Trakt client.</param>
    /// <param name="logger">The logger.</param>
    public TraktListGapSource(TraktClient trakt, ILogger<TraktListGapSource> logger)
    {
        _trakt = trakt;
        _logger = logger;
        _exploreDescriptors = new[]
        {
            new ExploreDescriptor
            {
                Kind = "traktlist",
                Label = "Trakt list",
                Source = this,
                // A list id is a string (a numeric id or a slug). The explore chip picker works in ints, so an
                // ad-hoc explore run reaches only numeric lists; a slug is entered in the settings field.
                Run = (context, ids, ct) => FindGapsForListsAsync(context, ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList(), ct),

                // Trakt has no list search yet (a follow-up could add one); a list is entered by raw id or slug.
                Search = null,
                Resolve = (id, ct) => _trakt.GetListNameAsync(id.ToString(CultureInfo.InvariantCulture), ct)
            }
        };
    }

    /// <inheritdoc />
    public string Name => "Trakt lists";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie, BaseItemKind.Series };

    /// <inheritdoc />
    public IReadOnlyCollection<ExploreDescriptor> ExploreDescriptors => _exploreDescriptors;

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanTraktLists
            && !string.IsNullOrWhiteSpace(config.TraktClientId)
            && ConfigIds.ParseTokens(config.CuratedTraktListIds).Count > 0;

    /// <inheritdoc />
    public IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        CancellationToken cancellationToken)
        => FindGapsForListsAsync(context, ConfigIds.ParseTokens(context.Config.CuratedTraktListIds), cancellationToken);

    /// <summary>
    /// Streams the gaps for an explicit set of list ids, diffed against the context's ownership index. The
    /// scan path calls this with the configured ids; an ad-hoc "explore a source" run calls it with one id
    /// the user picked, so a single list can be surfaced without a full rescan.
    /// </summary>
    /// <param name="context">The scan context.</param>
    /// <param name="listIds">The Trakt list ids or slugs to fetch and diff.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async stream of gaps.</returns>
    public async IAsyncEnumerable<GapItem> FindGapsForListsAsync(
        GapScanContext context,
        IReadOnlyList<string> listIds,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(listIds);

        var total = Math.Max(1, listIds.Count);
        var done = 0;

        foreach (var listId in listIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ServiceCircuit.IsOpen(ServiceNames.Trakt))
            {
                _logger.LogWarning("Trakt lists: service unavailable this run; skipping the remaining lists");
                break;
            }

            IReadOnlyList<TraktListItem> items;
            try
            {
                items = await _trakt.GetListItemsAsync(listId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Trakt lists: failed to fetch items for list {ListId}", listId);
                context.ReportProgress((double)++done / total);
                continue;
            }

            var name = await _trakt.GetListNameAsync(listId, cancellationToken).ConfigureAwait(false)
                ?? string.Create(CultureInfo.InvariantCulture, $"Trakt {listId}");
            _logger.LogInformation("Trakt lists: list '{Name}' ({Id}) has {Count} items", name, listId, items.Count);

            foreach (var gap in TraktListMapper.Build(listId, name, items, context.Ownership, MaxGapsPerList))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / total);
        }
    }
}
