using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Orchestrates a gap scan: runs every enabled source (via <see cref="GapScanPipeline"/>), backfills what a
/// capped or config-scoped source did not re-emit this run (via <see cref="GapBackfill"/> and its own
/// series-content pass), prunes what fell out of scope, enriches, and persists the resulting report. Ad-hoc
/// explore runs and on-demand re-checks (via <see cref="GapRecheckCoordinator"/>) share the same
/// carry-forward and link-enrichment steps as a full scan.
/// </summary>
public sealed class GapEngine
{
    private readonly ILibraryManager _libraryManager;
    private readonly IEnumerable<IGapSource> _sources;
    private readonly ExploreRegistry _explore;
    private readonly GapStore _store;
    private readonly ExternalLinkEnricher _externalLinks;
    private readonly Services.Webhook.WebhookNotifier _webhook;
    private readonly ResolutionStore _resolutions;
    private readonly OwnershipIndexBuilder _ownershipIndexBuilder;
    private readonly TmdbProviderLogos _providerLogos;
    private readonly GapScanPipeline _scanPipeline;
    private readonly GapRecheckCoordinator _recheck;
    private readonly ILogger<GapEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapEngine"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="sources">The registered gap sources.</param>
    /// <param name="explore">The explore-kind registry, for ad-hoc explore runs.</param>
    /// <param name="store">The gap store.</param>
    /// <param name="externalLinks">Folds the host's external-url providers into each gap's links.</param>
    /// <param name="webhook">Posts a completion notification, if a webhook is configured.</param>
    /// <param name="resolutions">Holds dismissals, including whole-creator dismissals not to carry forward.</param>
    /// <param name="ownershipIndexBuilder">Indexes the owned library for the sources to check candidates against.</param>
    /// <param name="providerLogos">Supplies streaming-provider logos for offers carried forward without one.</param>
    /// <param name="scanPipeline">Runs the enabled sources concurrently for a full scan.</param>
    /// <param name="recheck">Re-checks one or many owning items on demand, outside a full scan.</param>
    /// <param name="logger">The logger.</param>
    public GapEngine(
        ILibraryManager libraryManager,
        IEnumerable<IGapSource> sources,
        ExploreRegistry explore,
        GapStore store,
        ExternalLinkEnricher externalLinks,
        Services.Webhook.WebhookNotifier webhook,
        ResolutionStore resolutions,
        OwnershipIndexBuilder ownershipIndexBuilder,
        TmdbProviderLogos providerLogos,
        GapScanPipeline scanPipeline,
        GapRecheckCoordinator recheck,
        ILogger<GapEngine> logger)
    {
        _libraryManager = libraryManager;
        _sources = sources;
        _explore = explore;
        _store = store;
        _externalLinks = externalLinks;
        _webhook = webhook;
        _resolutions = resolutions;
        _ownershipIndexBuilder = ownershipIndexBuilder;
        _providerLogos = providerLogos;
        _scanPipeline = scanPipeline;
        _recheck = recheck;
        _logger = logger;
    }

    /// <summary>
    /// Runs all enabled sources and saves the resulting report.
    /// </summary>
    /// <param name="progress">Optional progress reporter (0-100).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generated report.</returns>
    public async Task<GapReport> RunAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var config = Plugin.RequireConfiguration();
        var enabled = _sources.Where(s => s.IsEnabled(config)).ToList();
        _logger.LogInformation(
            "Gap scan starting: {Count} of {Total} sources enabled [{Sources}]",
            enabled.Count,
            _sources.Count(),
            string.Join(", ", enabled.Select(s => s.Name)));
        var context = BuildContext(enabled, config);

        var priorReport = _store.Load();
        var priorIds = new HashSet<string>(priorReport.Items.Select(i => i.Id), StringComparer.Ordinal);

        var scanResult = await _scanPipeline.RunAsync(enabled, config, context.Ownership, priorReport, progress, cancellationToken).ConfigureAwait(false);
        var gaps = scanResult.Gaps.ToList();
        var byId = new Dictionary<string, GapItem>(gaps.Count, StringComparer.Ordinal);
        foreach (var gap in gaps)
        {
            byId[gap.Id] = gap;
        }

        // Carry the previous report's enrichment forward by id (resolved external ids and "where to
        // watch") so a rescan does not throw away what the background pass found; it only needs to look
        // up genuinely new gaps. Do this before the host link pass so carried ids produce their links.
        GapBackfill.CarryForward(gaps, priorReport.Items);

        // Backfill: filmography and recommendations only scan a slice of their seeds each run, so carry
        // forward prior gaps of those patterns that were not re-emitted this run and are still unowned.
        // Coverage then accumulates across runs instead of the un-scanned seeds' gaps vanishing. Gated on
        // the relevant source being enabled, so disabling it lets the accumulation drain on the next scan.
        if (config.ScanPeople || config.TraktEnabled)
        {
            LogBackfill(
                "CreatorWorks",
                GapBackfill.AccumulateUnowned(gaps, byId, priorReport.Items, context.Ownership, GapPattern.CreatorWorks, DismissedSourceItemIds(GapResolution.CreatorPrefix)));
        }

        if (config.ScanRecommendations)
        {
            LogBackfill(
                "Recommendation",
                GapBackfill.AccumulateUnowned(gaps, byId, priorReport.Items, context.Ownership, GapPattern.Recommendation, DismissedSourceItemIds(GapResolution.RecSourcePrefix)));
        }

        // The provider cross-checks scan only a slice of provider-resolvable series each run, so an episode
        // one of them found (that the library does not also track as a virtual episode) would vanish on a run
        // that did not re-check its series. Carry those forward, draining a carried gap once its series leaves
        // the library or the episode lands on disk.
        if (config.ScanSeries)
        {
            AccumulateSeriesContent(gaps, byId, priorReport.Items);
        }

        // The collection and discography sources scan everything each run rather than rotating a slice, so
        // they are carried forward only to survive a source that failed mid-scan (a TMDB or music-provider
        // blip that would otherwise blank a collection or discography from the saved report).
        LogBackfill(
            "set completion",
            GapBackfill.AccumulateSetCompletion(gaps, byId, priorReport.Items, context.Ownership, SetCompletionDomains(config)));

        // A non-rotating, config-scoped source (a keyword id removed, a whole watchlist source disabled)
        // fully re-derives its scope every run, unlike the accumulate passes above: "I did not produce
        // this gap this run" means it is genuinely out of scope, not merely "not yet this run's turn" the
        // way a rotating source's carry-forward means. Prune those now, after every accumulate pass has
        // run, so a gap a rotating source or AccumulateSetCompletion legitimately kept is never mistaken
        // for one of these.
        var staleIds = StaleOwnerPruner.FindStaleIds(gaps, _sources.OfType<IConfiguredScopeSource>(), config);
        if (staleIds.Count > 0)
        {
            gaps.RemoveAll(g => staleIds.Contains(g.Id));
            foreach (var id in staleIds)
            {
                byId.Remove(id);
            }
        }

        // A carried-forward gap keeps the offers it was stored with, and the availability pass skips a gap it
        // has already checked, so an offer stored without its provider's logo would never get one from a
        // lookup. Fill it in from the provider catalog, which is one cached read for every gap.
        if (config.IncludeAvailability)
        {
            var logos = await _providerLogos.GetAsync(config.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
            AvailabilityLogos.Fill(gaps, logos);
        }

        // Let the host's external-url providers contribute links (TMDB/IMDb from core, JustWatch from
        // that plugin if installed), keeping the hand-built links as a fallback for what core misses.
        _externalLinks.Enrich(gaps);

        // "Upcoming" is relative to today, not a property of the gap, and the accumulate passes above append
        // prior GapItem objects untouched. Re-derive it across the whole report so a carried-forward gap
        // does not keep an answer from the scan that first found it: a title whose release date has since
        // passed stops being upcoming, and one carried from before this was derived gets it at last.
        GapItemFactory.RefreshUpcoming(gaps);

        var report = new GapReport
        {
            GeneratedUtc = DateTime.UtcNow,
            GeneratedVersion = Plugin.Instance?.Version?.ToString() ?? string.Empty,
            TotalGaps = gaps.Count,
            Items = gaps,
            SourceRuns = scanResult.Runs
        };

        _store.Save(report);

        var newCount = gaps.Count(g => !priorIds.Contains(g.Id));
        _logger.LogInformation(
            "Gap scan complete: {Total} gaps ({New} new) saved in {Ms} ms",
            gaps.Count,
            newCount,
            stopwatch.ElapsedMilliseconds);

        await _webhook.NotifyAsync(
            "scan",
            string.Create(CultureInfo.InvariantCulture, $"Mind the Gaps scan finished: {gaps.Count} gaps ({newCount} new)."),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["totalGaps"] = gaps.Count,
                ["newGaps"] = newCount,
                ["generatedUtc"] = report.GeneratedUtc
            },
            cancellationToken).ConfigureAwait(false);

        return report;
    }

    /// <summary>
    /// Prunes gaps that a non-rotating, config-scoped source produced in the past but would not produce
    /// today (a removed keyword id, a disabled watchlist), without running a scan. Pure, config-only
    /// filtering over the current report, so it is safe to run on demand right after a config edit rather
    /// than waiting for the next scan to self-heal (which the automatic prune at the end of
    /// <see cref="RunAsync"/> already does).
    /// </summary>
    /// <returns>The number of gaps removed.</returns>
    public int PruneStaleGaps()
    {
        var config = Plugin.RequireConfiguration();
        var snapshot = _store.LoadSnapshot();
        var staleIds = StaleOwnerPruner.FindStaleIds(snapshot.Items, _sources.OfType<IConfiguredScopeSource>(), config);
        return staleIds.Count == 0 ? 0 : _store.RemoveGaps(staleIds);
    }

    /// <summary>
    /// Runs the source behind an explore kind ad-hoc against current ownership for an explicit set of ids,
    /// marks every gap it produces <see cref="GapItem.Adhoc"/>, and returns the report. This is the "explore
    /// a source" path: it does not accumulate un-scanned prior gaps, reconcile minted placeholders, or save
    /// (the caller merges the result additively), so it only ever surfaces this source's gaps for these ids.
    /// The ownership index is scoped to just that source's <see cref="IGapSource.OwnedKinds"/>. Supported
    /// kinds are those the registered sources declare (see <see cref="ExploreRegistry"/>).
    /// </summary>
    /// <param name="kind">The explore kind: "studio", "keyword", "tmdblist", "label", or "mdblist".</param>
    /// <param name="ids">The explicit descriptor ids to run the source for (for example MDBList list ids).</param>
    /// <param name="progress">Optional progress reporter (0-100).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A report of the ad-hoc gaps found.</returns>
    /// <exception cref="ArgumentException">The kind is not a supported explore kind.</exception>
    public async Task<GapReport> RunExploreAsync(string kind, IReadOnlyList<int> ids, IProgress<double>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(ids);

        var descriptor = _explore.Find(kind)
            ?? throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"'{kind}' is not a supported explore kind."),
                nameof(kind));

        var config = Plugin.RequireConfiguration();
        var ownership = _ownershipIndexBuilder.Build(descriptor.Source.OwnedKinds.Distinct().ToArray());
        var context = new GapScanContext(config, ownership);
        context.SetProgressSink(f => progress?.Report(Math.Clamp(f, 0.0, 1.0) * 100.0));

        _logger.LogInformation("Ad-hoc explore: running {Kind} source {Source} for {Count} id(s)", kind, descriptor.Source.Name, ids.Count);

        // Each scan starts with a clean circuit so a service given up on last run gets a fresh chance.
        Services.Http.ServiceCircuit.ResetAll();

        var gaps = new List<GapItem>();
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);

        await foreach (var gap in descriptor.Run(context, ids, ct).ConfigureAwait(false))
        {
            gap.Adhoc = true;
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

        // Re-adopt any external ids and "where to watch" the background pass resolved for these gaps before,
        // and rebuild the links those ids imply, so an explore run does not throw away that enrichment.
        GapBackfill.CarryForward(gaps, _store.Load().Items);

        // Let the host's external-url providers contribute links, as a full scan does.
        _externalLinks.Enrich(gaps);

        _logger.LogInformation("Ad-hoc explore: source {Source} produced {Count} gaps", descriptor.Source.Name, gaps.Count);

        return new GapReport
        {
            GeneratedUtc = DateTime.UtcNow,
            GeneratedVersion = Plugin.Instance?.Version?.ToString() ?? string.Empty,
            TotalGaps = gaps.Count,
            Items = gaps
        };
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
    public Task<int> RecheckManyAsync(IReadOnlyList<Guid> ownerIds, IProgress<double>? progress, CancellationToken cancellationToken)
        => _recheck.RecheckManyAsync(ownerIds, progress, cancellationToken);

    /// <summary>
    /// Gets the gap-id prefixes whose owning item can be re-checked on its own right now, which is what the
    /// dashboard offers after a verify leaves something still missing. Derived from the sources actually
    /// enabled, so a prefix disappears when its source is switched off rather than the page prompting for a
    /// pass this would then skip.
    /// </summary>
    /// <returns>The re-checkable gap-id prefixes.</returns>
    public IReadOnlyList<string> RecheckablePrefixes() => _recheck.RecheckablePrefixes();

    private void LogBackfill(string pattern, GapBackfill.BackfillResult result)
    {
        if (result.Carried > 0)
        {
            _logger.LogInformation("Backfill: carried {Carried} unowned {Pattern} gaps forward from the previous scan", result.Carried, pattern);
        }

        if (result.CappedOut)
        {
            _logger.LogInformation("Backfill: reached the accumulated cap for {Pattern}; older gaps not carried", pattern);
        }
    }

    private HashSet<string> DismissedSourceItemIds(string dismissedPrefix)
    {
        var dismissed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in _resolutions.GetAll().Keys)
        {
            if (id.StartsWith(dismissedPrefix, StringComparison.Ordinal))
            {
                dismissed.Add(id[dismissedPrefix.Length..]);
            }
        }

        return dismissed;
    }

    private static HashSet<MediaDomain> SetCompletionDomains(PluginConfiguration config)
    {
        var domains = new HashSet<MediaDomain>();
        if (config.ScanCollections)
        {
            domains.Add(MediaDomain.Movies);
        }

        if (config.ScanMusic || config.ScanDiscogs)
        {
            domains.Add(MediaDomain.Music);
        }

        return domains;
    }

    // Carry forward prior missing-episode gaps (SetCompletion, Episode) that no source re-emitted this run,
    // so a cross-check discovery survives runs that did not re-check its series. A carried gap drains when
    // its owning series is gone from the library, or the specific season/episode is now owned on disk.
    // The owned episodes of every series that has such a gap are read in one query, not one per series: a
    // library with a couple of thousand of these gaps spans hundreds of series. Stays here rather than in
    // GapBackfill because, unlike every other accumulate pass, it needs the library itself, not just config
    // and ownership.
    private void AccumulateSeriesContent(List<GapItem> gaps, Dictionary<string, GapItem> byId, IReadOnlyList<GapItem> prior)
    {
        const int maxAccumulated = 50000;

        var seriesExists = new Dictionary<Guid, bool>();
        var candidates = new List<(GapItem Item, Guid SeriesId, int Season, int Number)>();

        foreach (var item in prior)
        {
            if (item.Pattern != GapPattern.SetCompletion
                || item.TargetKind != BaseItemKind.Episode
                || byId.ContainsKey(item.Id))
            {
                continue;
            }

            if (!SeriesGapKey.TryParseEpisode(item.Id, out var season, out var number)
                || item.SourceItemId is null
                || !Guid.TryParseExact(item.SourceItemId, "N", out var seriesId))
            {
                continue;
            }

            if (!seriesExists.TryGetValue(seriesId, out var exists))
            {
                exists = _libraryManager.GetItemById(seriesId) is not null;
                seriesExists[seriesId] = exists;
            }

            if (exists)
            {
                candidates.Add((item, seriesId, season, number));
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var wanted = candidates.Select(c => c.SeriesId).ToHashSet();
        var ownedBySeries = OwnedEpisodeIndex.BySeries(
            _libraryManager.GetItemList(new InternalItemsQuery
            {
                DtoOptions = LibraryQueryOptions.Minimal(),
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsVirtualItem = false,
                Recursive = true
            }),
            wanted);

        var carried = 0;
        foreach (var (item, seriesId, season, number) in candidates)
        {
            if (byId.ContainsKey(item.Id)
                || (ownedBySeries.TryGetValue(seriesId, out var owned) && owned.Contains((season, number))))
            {
                continue;
            }

            if (carried >= maxAccumulated)
            {
                _logger.LogInformation("Backfill: reached the {Max} accumulated cap for series content; older gaps not carried", maxAccumulated);
                break;
            }

            byId[item.Id] = item;
            gaps.Add(item);
            carried++;
        }

        if (carried > 0)
        {
            _logger.LogInformation("Backfill: carried {Carried} unowned series-content gaps forward from the previous scan", carried);
        }
    }

    private GapScanContext BuildContext(IReadOnlyCollection<IGapSource> enabledSources, PluginConfiguration config)
    {
        // The kinds to index are declared by the sources themselves; the engine just unions them.
        var kinds = enabledSources.SelectMany(s => s.OwnedKinds).Distinct().ToArray();
        return new GapScanContext(config, _ownershipIndexBuilder.Build(kinds));
    }
}
