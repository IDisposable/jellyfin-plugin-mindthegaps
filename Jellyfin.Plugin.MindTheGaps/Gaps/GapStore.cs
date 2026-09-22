using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Persists the latest gap report to the plugin data folder and serves it back. This is the locking and
/// generation-counting state machine; how a report serializes to disk is <see cref="GapReportFiles"/>, and
/// the pure editing logic behind the partial-update methods is <see cref="GapReportEdits"/> - both kept
/// separate from the lock itself, since the store's state is only consistent because one lock serializes
/// every writer and the lock-free readers rely on it (see <see cref="AssertLocked"/>), and neither helper
/// touches that state.
/// </summary>
public sealed class GapStore
{
    // Coalesce the frequent checkpoint saves the background enrichment makes so a large report is not
    // fully rewritten every few lookups; the in-memory copy is always current, only the disk flush waits.
    private static readonly TimeSpan _minWriteInterval = TimeSpan.FromSeconds(5);

    private readonly ILogger<GapStore> _logger;
    private readonly string? _dataFolderOverride;
    private readonly object _lock = new();
    private GapReport? _cached;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    // What a cached copy of a response is validated against (see GetValidator). _generation counts every
    // change a reader could see, in-place merges included, which is why it cannot be inferred from the
    // _cached reference alone. The instance token keeps a restart from reusing a number a browser still
    // holds an ETag for. Written only under _lock, read without it: a validator check runs on every request
    // and must not queue behind a save, which holds the lock across its disk writes.
    private static readonly string _instanceToken = DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture);
    private long _generation;
    private long _lastChangedTicks;

    // A derived read index, not a second source of truth: _cached is warm for the life of the process, so
    // every domain-scoped read (a tab switch, a poll) would otherwise re-copy and re-filter every item
    // across every domain on every single call. Built once per _cached generation (tracked by reference,
    // the same generation _cached itself changes on any save) and reused until _cached is replaced.
    private GapReport? _domainIndexSource;
    private Dictionary<MediaDomain, GapItem[]>? _domainIndex;

    // Same idea for GetSummaryFacts: the dashboard calls it on every page load and after every
    // scan/mint/verify/availability pass, and it would otherwise rescan every gap in the report each time
    // just to produce a handful of counts and provider names. Keyed by _generation rather than the report
    // reference, because the provider names change when the availability pass merges into the cached
    // report in place.
    private long _summaryFactsGeneration = -1;
    private (IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> DomainPatternCounts, IReadOnlyList<string> Providers)? _summaryFacts;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapStore"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public GapStore(ILogger<GapStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GapStore"/> class with an explicit data folder.
    /// Test seam: lets a test persist into an isolated directory instead of the plugin data folder.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="dataFolder">The folder to persist the report into.</param>
    public GapStore(ILogger<GapStore> logger, string dataFolder)
        : this(logger)
    {
        _dataFolderOverride = dataFolder;
    }

    private string DataFolder
    {
        get
        {
            var dataFolder = _dataFolderOverride ?? Plugin.Instance?.DataFolderPath ?? Path.GetTempPath();
            Directory.CreateDirectory(dataFolder);
            return dataFolder;
        }
    }

    /// <summary>
    /// Saves the report: caches it in memory and flushes it to disk atomically.
    /// </summary>
    /// <param name="report">The report to save.</param>
    public void Save(GapReport report)
    {
        lock (_lock)
        {
            Publish(report);
            Flush(report, null);
        }
    }

    /// <summary>
    /// Writes a mid-scan checkpoint to disk without replacing the cached report. The cache stays the prior
    /// report (so the engine's carry-forward and the dashboard keep reading it during the scan), while disk
    /// holds the latest progress so a crash or shutdown mid-scan does not lose the batch. The engine throttles
    /// how often it calls this. The final <see cref="Save(GapReport)"/> updates both cache and disk as usual.
    /// </summary>
    /// <param name="report">The in-progress report to persist.</param>
    public void SaveCheckpoint(GapReport report)
    {
        lock (_lock)
        {
            Flush(report, null);
        }
    }

    /// <summary>
    /// Folds the availability enrichment from a background pass into the current report by gap id, then
    /// flushes. If the pass's report is still the cached one this is an ordinary save; if a scan replaced
    /// the cached report while the pass was running, the enrichment (offers, the checked flag, resolved
    /// external ids and their links) lands on the new report instead of overwriting it with the older
    /// captured copy. That is the lost update a long background pass would otherwise cause.
    /// </summary>
    /// <param name="report">The report the pass has been enriching.</param>
    /// <param name="throttle">When true, flush only if past the coalescing interval (checkpoint saves).</param>
    public void SaveAvailabilityMerge(GapReport report, bool throttle)
    {
        lock (_lock)
        {
            GapReport current;
            if (_cached is null || ReferenceEquals(_cached, report))
            {
                Publish(report);
                current = report;
            }
            else
            {
                GapReportEdits.MergeAvailability(report, _cached);
                Changed(DateTime.UtcNow);
                current = _cached;
            }

            if (!throttle || DateTime.UtcNow - _lastWriteUtc >= _minWriteInterval)
            {
                Flush(current, null);
            }
        }
    }

    /// <summary>
    /// Additively merges an ad-hoc run's gaps into the current report by id and flushes. A gap not already
    /// present is appended; a gap that re-appears keeps the prior row's "where to watch" enrichment; and
    /// every gap from other sources is left untouched. Unlike a full scan this never drops gaps it did not
    /// find, so a one-off "explore a source" run only ever adds to the report.
    /// </summary>
    /// <param name="toAdd">The ad-hoc run's report whose gaps are merged in.</param>
    /// <returns>The number of gaps that were newly added (not already in the report).</returns>
    public int MergeAdditiveGaps(GapReport toAdd)
    {
        ArgumentNullException.ThrowIfNull(toAdd);

        lock (_lock)
        {
            var current = LoadLocked();
            var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
            var order = new List<string>(current.Items.Count + toAdd.Items.Count);
            foreach (var item in current.Items)
            {
                if (byId.TryAdd(item.Id, item))
                {
                    order.Add(item.Id);
                }
            }

            var added = 0;
            foreach (var add in toAdd.Items)
            {
                if (byId.TryGetValue(add.Id, out var prior))
                {
                    // Already in the report. Carry the prior gap's enrichment forward, fold its source onto the
                    // freshly explored gap (so a curated list claims a title its recommendation already
                    // surfaced, rather than the run looking like it did nothing), and keep the gap real if the
                    // prior one was, so clearing the ad-hoc run does not drop a genuine scan gap.
                    GapReportEdits.CarryEnrichment(prior, add);
                    GapSourceMerge.Merge(add, prior);
                    add.Adhoc = add.Adhoc && prior.Adhoc;
                }
                else
                {
                    order.Add(add.Id);
                    added++;
                }

                byId[add.Id] = add;
            }

            var items = order.ConvertAll(id => byId[id]);
            var report = new GapReport
            {
                GeneratedUtc = current.GeneratedUtc,
                GeneratedVersion = current.GeneratedVersion,
                TotalGaps = items.Count,
                Items = items,
                SourceRuns = current.SourceRuns
            };
            Publish(report);
            Flush(report, GapReportEdits.DomainsOf(toAdd.Items));
            return added;
        }
    }

    /// <summary>
    /// Removes the ad-hoc "explore a source" gaps from the current report and flushes. When
    /// <paramref name="sourceItemId"/> is given, only ad-hoc gaps surfaced by that owning item are removed;
    /// otherwise every ad-hoc gap is removed. Permanent (scanned) gaps are left untouched.
    /// </summary>
    /// <param name="sourceItemId">The owning item id to scope the clear to, or null to clear all ad-hoc gaps.</param>
    /// <returns>The number of gaps removed.</returns>
    public int RemoveAdhocGaps(string? sourceItemId)
    {
        lock (_lock)
        {
            var current = LoadLocked();
            var kept = new List<GapItem>(current.Items.Count);
            var dirtyDomains = new HashSet<MediaDomain>();
            var removed = 0;
            foreach (var item in current.Items)
            {
                if (item.Adhoc
                    && (sourceItemId is null || string.Equals(item.SourceItemId, sourceItemId, StringComparison.Ordinal)))
                {
                    removed++;
                    dirtyDomains.Add(item.Domain);
                    continue;
                }

                kept.Add(item);
            }

            if (removed == 0)
            {
                return 0;
            }

            var report = new GapReport
            {
                GeneratedUtc = current.GeneratedUtc,
                GeneratedVersion = current.GeneratedVersion,
                TotalGaps = kept.Count,
                Items = kept,
                SourceRuns = current.SourceRuns
            };
            Publish(report);
            Flush(report, dirtyDomains);
            return removed;
        }
    }

    /// <summary>
    /// Removes the gaps with the given ids and saves, leaving every other gap untouched. Backs the report's
    /// verify actions, which drop the rows the library has since been given. The report's scan time and
    /// version are preserved (a verify is a partial update, not a new scan).
    /// </summary>
    /// <param name="ids">The gap ids to drop.</param>
    /// <returns>The number of gaps removed.</returns>
    public int RemoveGaps(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var drop = new HashSet<string>(ids, StringComparer.Ordinal);
        if (drop.Count == 0)
        {
            return 0;
        }

        lock (_lock)
        {
            var current = LoadLocked();
            var kept = new List<GapItem>(current.Items.Count);
            var dirtyDomains = new HashSet<MediaDomain>();
            var removed = 0;
            foreach (var item in current.Items)
            {
                if (drop.Contains(item.Id))
                {
                    removed++;
                    dirtyDomains.Add(item.Domain);
                    continue;
                }

                kept.Add(item);
            }

            if (removed == 0)
            {
                return 0;
            }

            var report = new GapReport
            {
                GeneratedUtc = current.GeneratedUtc,
                GeneratedVersion = current.GeneratedVersion,
                TotalGaps = kept.Count,
                Items = kept,
                SourceRuns = current.SourceRuns
            };
            Publish(report);
            Flush(report, dirtyDomains);
            return removed;
        }
    }

    /// <summary>
    /// Replaces one owning item's gaps with a fresh re-check and saves, leaving every other gap untouched.
    /// Used by the per-source re-check so a fix or an acquisition can be verified without a full rescan;
    /// unlike an additive merge, this also drops gaps the fix resolved. The report's scan time and version
    /// are preserved (a re-check is a partial update, not a new scan).
    /// </summary>
    /// <param name="sourceItemId">The owning item whose gaps are being replaced.</param>
    /// <param name="idPrefixes">
    /// The gap-id prefixes the re-checking sources produce. Only gaps carrying both this owning item and one
    /// of these prefixes are swapped, so a re-check of (say) a collection cannot disturb a recommendation or
    /// a filmography entry the same owning item also seeded.
    /// </param>
    /// <param name="recheck">The freshly computed gaps for that owning item.</param>
    /// <returns>The updated report.</returns>
    public GapReport ReplaceSourceGaps(string sourceItemId, IReadOnlyCollection<string> idPrefixes, GapReport recheck)
    {
        ArgumentNullException.ThrowIfNull(idPrefixes);
        ArgumentNullException.ThrowIfNull(recheck);

        lock (_lock)
        {
            var current = LoadLocked();
            var kept = new List<GapItem>(current.Items.Count + recheck.Items.Count);
            var dirtyDomains = new HashSet<MediaDomain>(GapReportEdits.DomainsOf(recheck.Items));
            foreach (var item in current.Items)
            {
                if (GapReportEdits.IsReplaced(item, sourceItemId, idPrefixes))
                {
                    dirtyDomains.Add(item.Domain);
                }
                else
                {
                    kept.Add(item);
                }
            }

            kept.AddRange(recheck.Items);

            var report = new GapReport
            {
                GeneratedUtc = current.GeneratedUtc,
                GeneratedVersion = current.GeneratedVersion,
                TotalGaps = kept.Count,
                Items = kept,
                SourceRuns = current.SourceRuns
            };
            Publish(report);
            Flush(report, dirtyDomains);
            return report;
        }
    }

    // Writes only the domains dirtyDomains names (null means every implemented domain: a full scan or a
    // multi-source pass genuinely touches all of them, so there is nothing to gain from tracking it there).
    // Caller holds _lock.
    private void Flush(GapReport report, IReadOnlySet<MediaDomain>? dirtyDomains)
    {
        AssertLocked();
        try
        {
            var dataFolder = DataFolder;
            GapReportFiles.WriteDirtyDomains(dataFolder, report.Items, dirtyDomains);
            GapReportFiles.WriteMeta(dataFolder, report);
            GapReportFiles.DeleteLegacyIfPresent(dataFolder);
            _lastWriteUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist gap report");
        }
    }

    /// <summary>
    /// Gets what a cached copy of anything derived from the report is validated against: a tag that changes
    /// whenever the report a reader would see changes, and when that last happened. The tag is unique to this
    /// process, so it cannot collide with one a browser holds from before a restart. Takes no lock and reads
    /// nothing from disk. Read it before reading the report: a change in between then only makes the next
    /// request miss, where the other order could pair old data with a newer tag and let a stale copy
    /// revalidate indefinitely. Before the report has been loaded the generation is zero, and loading it counts
    /// as a change, so a response computed across that first load is simply not matched by the next request.
    /// </summary>
    /// <returns>The change tag and the UTC time of the last change (the scan time, until something else changes it).</returns>
    public (string Tag, DateTime LastChangedUtc) GetValidator()
    {
        var (generation, changedUtc) = GetGeneration();
        return (string.Concat(_instanceToken, ".", generation.ToString(CultureInfo.InvariantCulture)), changedUtc);
    }

    /// <summary>
    /// Gets the count of changes to the report, for derived reads to memoize against, with when the latest
    /// happened. The two are read together because they only mean something as a pair.
    /// </summary>
    /// <returns>The generation, and the UTC time of the change that produced it (<see cref="DateTime.MinValue"/> before the report has loaded).</returns>
    public (long Generation, DateTime ChangedUtc) GetGeneration()
    {
        // The count first: a change writes its time before bumping the count, so a reader that sees a
        // count never pairs it with an older time.
        var generation = Volatile.Read(ref _generation);
        var ticks = Volatile.Read(ref _lastChangedTicks);
        return (generation, ticks == 0 ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Utc));
    }

    // Replaces the served report. Caller holds _lock.
    private void Publish(GapReport report)
    {
        AssertLocked();
        _cached = report;
        Changed(DateTime.UtcNow);
    }

    // Records a change a reader could see. The time is written first, so a reader that sees the new
    // generation never pairs it with an older time, and it only moves forward: a cold load stamps the scan
    // time, which must not pull it back behind a change already recorded. Caller holds _lock.
    private void Changed(DateTime whenUtc)
    {
        AssertLocked();
        Volatile.Write(ref _lastChangedTicks, Math.Max(whenUtc.Ticks, _lastChangedTicks));
        Interlocked.Increment(ref _generation);
    }

    // The store's state is only consistent because one lock serializes every writer, and the lock-free
    // readers rely on it. A private method whose contract is "caller holds _lock" states it here, so a new
    // caller outside the lock fails in every Debug test run instead of racing.
    [Conditional("DEBUG")]
    private void AssertLocked() => Debug.Assert(Monitor.IsEntered(_lock), "The store lock must be held.");

    /// <summary>
    /// Loads the latest report (from memory if available, otherwise disk).
    /// </summary>
    /// <returns>The latest report, or an empty report if none exists.</returns>
    public GapReport Load()
    {
        lock (_lock)
        {
            return LoadLocked();
        }
    }

    /// <summary>
    /// Loads the latest report together with the generation it belongs to, under one lock, for a caller that
    /// memoizes something derived from the report. Reading the two separately takes the lock twice and lets a
    /// change land between them, pairing a report with the wrong generation.
    /// </summary>
    /// <returns>The latest report (or an empty one) and its generation.</returns>
    public (GapReport Report, long Generation) LoadWithGeneration()
    {
        lock (_lock)
        {
            var report = LoadLocked();
            return (report, _generation);
        }
    }

    // Load for a caller that already holds _lock.
    private GapReport LoadLocked()
    {
        AssertLocked();
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            _cached = GapReportFiles.Load(DataFolder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read gap report");
        }

        if (_cached is not null)
        {
            Changed(_cached.GeneratedUtc);
        }

        return _cached ?? new GapReport { GeneratedUtc = DateTime.MinValue };
    }

    /// <summary>
    /// Returns a read snapshot of the current report: a new report wrapping a fresh copy of the items
    /// list (the gap objects are shared, not deep-copied). Read paths (the API) use this so a response is
    /// never the live cached list that a scan can replace, or the availability pass can be mutating,
    /// mid-serialize. The per-item field writes the pass makes are atomic reference/scalar writes, so a
    /// shared item never serializes torn; the snapshot only decouples the list itself.
    /// </summary>
    /// <returns>A snapshot of the latest report.</returns>
    public GapReport LoadSnapshot()
    {
        lock (_lock)
        {
            var report = LoadLocked();
            return new GapReport
            {
                GeneratedUtc = report.GeneratedUtc,
                GeneratedVersion = report.GeneratedVersion,
                TotalGaps = report.TotalGaps,
                Items = report.Items.ToArray(),
                SourceRuns = report.SourceRuns
            };
        }
    }

    /// <summary>
    /// Returns a read snapshot scoped to one domain, for the dashboard's per-tab load (it already asks
    /// for one domain at a time). Unlike <see cref="LoadSnapshot"/>, this never copies or filters the
    /// other domains' items: a lookup into a read index built once per report generation and reused
    /// across calls, not a fresh scan of the whole report on every request.
    /// </summary>
    /// <param name="domain">The domain to scope the snapshot to.</param>
    /// <returns>A snapshot of the latest report, containing only that domain's gaps.</returns>
    public GapReport LoadDomainSnapshot(MediaDomain domain)
    {
        lock (_lock)
        {
            var report = LoadLocked();
            if (_domainIndex is null || !ReferenceEquals(_domainIndexSource, report))
            {
                var byDomain = new Dictionary<MediaDomain, List<GapItem>>();
                foreach (var d in MediaDomains.Implemented)
                {
                    byDomain[d] = new List<GapItem>();
                }

                foreach (var item in report.Items)
                {
                    if (byDomain.TryGetValue(item.Domain, out var list))
                    {
                        list.Add(item);
                    }
                }

                _domainIndex = byDomain.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
                _domainIndexSource = report;
            }

            return new GapReport
            {
                GeneratedUtc = report.GeneratedUtc,
                GeneratedVersion = report.GeneratedVersion,
                TotalGaps = report.TotalGaps,
                Items = _domainIndex.TryGetValue(domain, out var items) ? items : [],
                SourceRuns = report.SourceRuns
            };
        }
    }

    /// <summary>
    /// Returns the per-domain-and-pattern gap counts and the distinct availability provider names seen
    /// anywhere in the report, computed once per report generation and reused across repeated calls
    /// rather than rescanning every gap on every call (<c>GetSummary</c> is called on every page load and
    /// after every scan/mint/verify/availability pass).
    /// </summary>
    /// <returns>The counts, keyed by domain name then pattern name, and the sorted provider names.</returns>
    public (IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> DomainPatternCounts, IReadOnlyList<string> Providers) GetSummaryFacts()
    {
        lock (_lock)
        {
            var report = LoadLocked();
            if (_summaryFacts is null || _summaryFactsGeneration != _generation)
            {
                var patternCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
                var providers = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var item in report.Items)
                {
                    if (!patternCounts.TryGetValue(item.DomainName, out var byPattern))
                    {
                        byPattern = new Dictionary<string, int>(StringComparer.Ordinal);
                        patternCounts[item.DomainName] = byPattern;
                    }

                    byPattern.TryGetValue(item.PatternName, out var pc);
                    byPattern[item.PatternName] = pc + 1;

                    foreach (var offer in item.Availability)
                    {
                        if (!string.IsNullOrEmpty(offer.Provider))
                        {
                            providers.Add(offer.Provider);
                        }
                    }
                }

                _summaryFacts = (
                    patternCounts.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, int>)kv.Value, StringComparer.Ordinal),
                    providers.ToArray());
                _summaryFactsGeneration = _generation;
            }

            return _summaryFacts.Value;
        }
    }

    /// <summary>
    /// Finds a gap by its stable id in the current report, or null when the id is blank or absent. The API
    /// uses this to rehydrate a gap server-side rather than trusting a client-posted gap body.
    /// </summary>
    /// <param name="id">The gap id, or null.</param>
    /// <returns>The gap, or null.</returns>
    public GapItem? FindById(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        lock (_lock)
        {
            return LoadLocked().Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));
        }
    }
}
