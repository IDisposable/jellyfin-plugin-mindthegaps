using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Re-checks one or many owning library items on demand (a per-set "recheck" button, or a heading-level
/// bulk sweep) without running a full scan. Split out of <see cref="GapEngine"/> because it is a complete,
/// self-contained concern: resolving owning items, picking their claiming sources (or the series-content
/// sources for a series), running just those, and swapping the result into the store by id prefix.
/// </summary>
public sealed class GapRecheckCoordinator
{
    // Every series-content gap carries this prefix, so a series re-check swaps exactly those and leaves a
    // recommendation the series happens to seed alone.
    private static readonly string[] SeriesPrefixes = ["seriescontent:"];

    private readonly ILibraryManager _libraryManager;
    private readonly IEnumerable<IGapSource> _sources;
    private readonly GapStore _store;
    private readonly ExternalLinkEnricher _externalLinks;
    private readonly OwnershipIndexBuilder _ownershipIndexBuilder;
    private readonly ILogger<GapRecheckCoordinator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapRecheckCoordinator"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager, for resolving owning items.</param>
    /// <param name="sources">The registered gap sources.</param>
    /// <param name="store">The gap store, into which a re-checked owner's gaps are swapped.</param>
    /// <param name="externalLinks">Folds the host's external-url providers into each gap's links.</param>
    /// <param name="ownershipIndexBuilder">Indexes the owned library for the batch's ownership check.</param>
    /// <param name="logger">The logger.</param>
    public GapRecheckCoordinator(
        ILibraryManager libraryManager,
        IEnumerable<IGapSource> sources,
        GapStore store,
        ExternalLinkEnricher externalLinks,
        OwnershipIndexBuilder ownershipIndexBuilder,
        ILogger<GapRecheckCoordinator> logger)
    {
        _libraryManager = libraryManager;
        _sources = sources;
        _store = store;
        _externalLinks = externalLinks;
        _ownershipIndexBuilder = ownershipIndexBuilder;
        _logger = logger;
    }

    /// <summary>
    /// Re-checks a batch of owning items and swaps each one's gaps into the report as it goes, so the
    /// dashboard can re-check every set under a heading ("Studios", "Collections and franchises") in one
    /// pass. The ownership index is built once for the whole batch rather than per item, which is what makes
    /// this cheaper than re-checking each item on its own.
    /// </summary>
    /// <param name="ownerIds">The owning library items to re-check.</param>
    /// <param name="progress">Progress sink (0-100).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many owning items were actually re-checked (those with no claiming source are skipped).</returns>
    public async Task<int> RecheckManyAsync(IReadOnlyList<Guid> ownerIds, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerIds);

        var config = Plugin.RequireConfiguration();

        // Resolve and claim first, so the one ownership index covers every kind the batch will diff against.
        // A series is claimed by no set source; it goes through the series-content sources instead, so a
        // "re-check everything under this heading" works on the Shows tab as well as the Movies one.
        var work = new List<(BaseItem Owner, List<ISetContentSource> Claimants)>(ownerIds.Count);
        foreach (var ownerId in ownerIds)
        {
            var owner = _libraryManager.GetItemById(ownerId);
            if (owner is null)
            {
                continue;
            }

            var claimants = Claimants(owner, config);
            if (claimants.Count > 0 || owner.GetBaseItemKind() == BaseItemKind.Series)
            {
                work.Add((owner, claimants));
            }
        }

        if (work.Count == 0)
        {
            _logger.LogInformation("Bulk re-check: nothing to do; no enabled source claims any of the {Count} item(s)", ownerIds.Count);
            return 0;
        }

        var context = new GapScanContext(config, _ownershipIndexBuilder.Build(OwnedKindsOf(work.SelectMany(w => w.Claimants))));

        var done = 0;
        foreach (var (owner, claimants) in work)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<GapItem> gaps;
            IReadOnlyCollection<string> prefixes;
            if (claimants.Count > 0)
            {
                (gaps, prefixes) = await RunClaimantsAsync(owner, claimants, context, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                gaps = await RunSeriesSourcesAsync(owner, config, context, cancellationToken).ConfigureAwait(false);
                prefixes = SeriesPrefixes;
            }

            // Swap each item in as it finishes rather than at the end, so a cancelled or failed batch still
            // leaves the items it got through up to date.
            _store.ReplaceSourceGaps(
                owner.Id.ToString("N", CultureInfo.InvariantCulture),
                prefixes,
                new GapReport { TotalGaps = gaps.Count, Items = gaps });

            done++;
            progress?.Report((double)done / work.Count * 100.0);
        }

        _logger.LogInformation("Bulk re-check: re-checked {Done} of {Asked} item(s)", done, ownerIds.Count);
        return done;
    }

    /// <summary>
    /// Gets the gap-id prefixes whose owning item can be re-checked on its own right now, which is what the
    /// dashboard offers after a verify leaves something still missing. Derived from the sources actually
    /// enabled, so a prefix disappears when its source is switched off rather than the page prompting for a
    /// pass this would then skip.
    /// </summary>
    /// <returns>The re-checkable gap-id prefixes.</returns>
    public IReadOnlyList<string> RecheckablePrefixes()
    {
        var config = Plugin.RequireConfiguration();
        var prefixes = new List<string>();

        foreach (var source in _sources.OfType<ISetContentSource>())
        {
            if (((IGapSource)source).IsEnabled(config))
            {
                prefixes.Add(source.GapIdPrefix);
            }
        }

        // Series are re-checked through the series-content sources rather than a set source, so their
        // prefix is added on the same terms: only when something is enabled to answer for them.
        if (_sources.OfType<ISeriesContentSource>().Any(s => ((IGapSource)s).IsEnabled(config)))
        {
            prefixes.AddRange(SeriesPrefixes);
        }

        return prefixes;
    }

    // The enabled set sources that produce gaps for this owning item. Empty for an item no source handles
    // (a Person filmography, a recommendation seed), which is what makes a mis-aimed re-check a no-op
    // rather than a swap that wipes the item's gaps.
    private List<ISetContentSource> Claimants(BaseItem? owner, PluginConfiguration config)
        => owner is null
            ? []
            : _sources.OfType<ISetContentSource>()
                .Where(s => ((IGapSource)s).IsEnabled(config) && s.Claims(owner))
                .ToList();

    private static BaseItemKind[] OwnedKindsOf(IEnumerable<ISetContentSource> sources)
        => sources.SelectMany(s => ((IGapSource)s).OwnedKinds).Distinct().ToArray();

    // Runs the enabled series-content sources for one owned series, de-duping across them and carrying prior
    // enrichment forward. Shared by the single-series re-check and the bulk one.
    private async Task<List<GapItem>> RunSeriesSourcesAsync(
        BaseItem series,
        PluginConfiguration config,
        GapScanContext context,
        CancellationToken cancellationToken)
    {
        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);

        foreach (var source in _sources.OfType<ISeriesContentSource>())
        {
            if (!((IGapSource)source).IsEnabled(config))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<GapItem> found;
            try
            {
                found = await source.CheckSeriesAsync(series, context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One cross-check being unreachable must not abort the re-check; the others still count.
                _logger.LogWarning(ex, "Re-check: {Source} failed for {Series}", ((IGapSource)source).Name, series.Name);
                continue;
            }

            foreach (var gap in found)
            {
                if (byId.TryGetValue(gap.Id, out var existing))
                {
                    GapSourceMerge.Merge(existing, gap);
                }
                else
                {
                    byId[gap.Id] = gap;
                    gaps.Add(gap);
                }
            }
        }

        // Re-adopt the external ids and "where to watch" a prior pass resolved for these gaps, and let the
        // host's external-url providers contribute links, exactly as a full scan and an explore do.
        GapBackfill.CarryForward(gaps, _store.Load().Items);
        _externalLinks.Enrich(gaps);

        _logger.LogInformation("Re-check: {Series} has {Count} missing-episode gap(s)", series.Name, gaps.Count);
        return gaps;
    }

    // Runs every claiming source for one owning item, de-duping across them and carrying prior enrichment
    // forward, exactly as a scan and an explore do. Returns the gaps and the id prefixes to swap out.
    private async Task<(List<GapItem> Gaps, HashSet<string> Prefixes)> RunClaimantsAsync(
        BaseItem owner,
        List<ISetContentSource> claimants,
        GapScanContext context,
        CancellationToken cancellationToken)
    {
        var gaps = new List<GapItem>();
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);

        foreach (var source in claimants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<GapItem>? found;
            try
            {
                found = await source.CheckOneAsync(owner, context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One provider being unreachable must not abort the re-check; the others still count.
                _logger.LogWarning(ex, "Re-check: {Source} failed for {Owner}", ((IGapSource)source).Name, owner.Name);
                found = null;
            }

            // A source only claims its prefix when it actually answered. Claiming it after a failure would
            // hand ReplaceSourceGaps an empty result to swap in, deleting this owner's real gaps because a
            // provider happened to be down.
            if (found is null)
            {
                _logger.LogInformation(
                    "Re-check: {Source} could not determine {Owner}; leaving its existing gaps in place",
                    ((IGapSource)source).Name,
                    owner.Name);
                continue;
            }

            prefixes.Add(source.GapIdPrefix);

            foreach (var gap in found)
            {
                if (byId.TryGetValue(gap.Id, out var existing))
                {
                    GapSourceMerge.Merge(existing, gap);
                }
                else
                {
                    byId[gap.Id] = gap;
                    gaps.Add(gap);
                }
            }
        }

        GapBackfill.CarryForward(gaps, _store.Load().Items);
        _externalLinks.Enrich(gaps);

        _logger.LogInformation("Re-check: {Owner} has {Count} gap(s) across {Sources} source(s)", owner.Name, gaps.Count, claimants.Count);
        return (gaps, prefixes);
    }
}
