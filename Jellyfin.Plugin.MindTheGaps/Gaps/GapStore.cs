using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Persists the latest gap report to the plugin data folder and serves it back.
/// </summary>
public sealed class GapStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    // Coalesce the frequent checkpoint saves the background enrichment makes so a large report is not
    // fully rewritten every few lookups; the in-memory copy is always current, only the disk flush waits.
    private static readonly TimeSpan _minWriteInterval = TimeSpan.FromSeconds(5);

    // The lowercase file-name segment for each domain, computed once rather than on every DomainFilePath
    // call (a Flush touches this per domain on every save).
    private static readonly IReadOnlyDictionary<MediaDomain, string> _domainFileNames =
        MediaDomains.Implemented.ToDictionary(d => d, d => d.ToString().ToLowerInvariant());

    private readonly ILogger<GapStore> _logger;
    private readonly string? _dataFolderOverride;
    private readonly object _lock = new();
    private GapReport? _cached;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    // A derived read index, not a second source of truth: _cached is warm for the life of the process, so
    // every domain-scoped read (a tab switch, a poll) would otherwise re-copy and re-filter every item
    // across every domain on every single call. Built once per _cached generation (tracked by reference,
    // the same generation _cached itself changes on any save) and reused until _cached is replaced.
    private GapReport? _domainIndexSource;
    private Dictionary<MediaDomain, GapItem[]>? _domainIndex;

    // Same idea for GetSummaryFacts: the dashboard calls it on every page load and after every
    // scan/mint/verify/availability pass, and it would otherwise rescan every gap in the report each time
    // just to produce a handful of counts and provider names.
    private GapReport? _summaryFactsSource;
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

    // The report used to be one gaps.json; splitting it by domain means a small, frequent update (a
    // Verify, a Send) only has to rewrite the one domain file it actually touched instead of the whole
    // report. SourceRuns carries no domain (a run is not scoped to one), so it lives in the shared meta
    // file alongside the scan timestamp/version rather than being shardable itself.
    private static string LegacyFilePath(string dataFolder) => Path.Combine(dataFolder, "gaps.json");

    private static string MetaFilePath(string dataFolder) => Path.Combine(dataFolder, "gaps-meta.json");

    private static string DomainFilePath(string dataFolder, MediaDomain domain)
        => Path.Combine(dataFolder, string.Concat("gaps-", _domainFileNames[domain], ".json"));

    /// <summary>
    /// Saves the report: caches it in memory and flushes it to disk atomically.
    /// </summary>
    /// <param name="report">The report to save.</param>
    public void Save(GapReport report)
    {
        lock (_lock)
        {
            _cached = report;
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
                _cached = report;
                current = report;
            }
            else
            {
                MergeAvailability(report, _cached);
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
            var current = Load();
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
                    CarryEnrichment(prior, add);
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
            _cached = report;
            Flush(report, DomainsOf(toAdd.Items));
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
            var current = Load();
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
            _cached = report;
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
            var current = Load();
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
            _cached = report;
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
            var current = Load();
            var kept = new List<GapItem>(current.Items.Count + recheck.Items.Count);
            var dirtyDomains = new HashSet<MediaDomain>(DomainsOf(recheck.Items));
            foreach (var item in current.Items)
            {
                if (IsReplaced(item, sourceItemId, idPrefixes))
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
            _cached = report;
            Flush(report, dirtyDomains);
            return report;
        }
    }

    private static bool IsReplaced(GapItem item, string sourceItemId, IReadOnlyCollection<string> idPrefixes)
    {
        if (!string.Equals(item.SourceItemId, sourceItemId, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var prefix in idPrefixes)
        {
            if (item.Id.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<MediaDomain> DomainsOf(IEnumerable<GapItem> items)
        => new(items.Select(i => i.Domain));

    // Keep an ad-hoc re-run of the same source from discarding a "where to watch" result the background
    // pass already found for a gap (the lookup is the costly part); the rest comes fresh from the source.
    private static void CarryEnrichment(GapItem prior, GapItem fresh)
    {
        if (!prior.AvailabilityChecked)
        {
            return;
        }

        fresh.AvailabilityChecked = true;
        if (fresh.Availability.Count == 0 && prior.Availability.Count > 0)
        {
            fresh.Availability = prior.Availability;
        }
    }

    // Copy the fields the availability pass produces from one report's items onto a (newer) report's
    // items, matched by id. Items the newer report does not have (resolved or acquired since) are skipped;
    // items it has that the pass did not touch keep their values.
    private static void MergeAvailability(GapReport from, GapReport into)
    {
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        foreach (var item in from.Items)
        {
            byId[item.Id] = item;
        }

        foreach (var target in into.Items)
        {
            if (!byId.TryGetValue(target.Id, out var source))
            {
                continue;
            }

            target.AvailabilityChecked = source.AvailabilityChecked;
            if (source.Availability.Count > 0)
            {
                target.Availability = source.Availability;
            }

            target.ProviderIds = source.ProviderIds;
            target.Links = source.Links;
        }
    }

    // Writes only the domains in dirtyDomains (null means every implemented domain: a full scan or a
    // multi-source pass genuinely touches all of them, so there is nothing to gain from tracking it there).
    // Atomic per file: serialize to a temp file then replace, so a crash mid-write cannot truncate or lose
    // a domain's gaps. Caller holds _lock.
    private void Flush(GapReport report, IReadOnlySet<MediaDomain>? dirtyDomains)
    {
        try
        {
            var dataFolder = DataFolder;
            var byDomain = new Dictionary<MediaDomain, List<GapItem>>();
            foreach (var domain in MediaDomains.Implemented)
            {
                byDomain[domain] = new List<GapItem>();
            }

            foreach (var item in report.Items)
            {
                if (byDomain.TryGetValue(item.Domain, out var items))
                {
                    items.Add(item);
                }
            }

            foreach (var domain in MediaDomains.Implemented)
            {
                if (dirtyDomains is not null && !dirtyDomains.Contains(domain))
                {
                    continue;
                }

                WriteDomainFile(dataFolder, domain, byDomain[domain]);
            }

            var meta = new GapReport
            {
                GeneratedUtc = report.GeneratedUtc,
                GeneratedVersion = report.GeneratedVersion,
                TotalGaps = report.TotalGaps,
                SourceRuns = report.SourceRuns
            };
            WriteJson(MetaFilePath(dataFolder), meta);

            // Once the split files exist, the legacy monolith is never read again; keep it from lingering
            // as a second, increasingly stale, on-disk copy of the report.
            var legacy = LegacyFilePath(dataFolder);
            if (File.Exists(legacy))
            {
                File.Delete(legacy);
            }

            _lastWriteUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist gap report");
        }
    }

    private static void WriteDomainFile(string dataFolder, MediaDomain domain, IReadOnlyList<GapItem> items)
    {
        var path = DomainFilePath(dataFolder, domain);
        if (items.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        WriteJson(path, items);
    }

    private static void WriteJson<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, _jsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Loads the latest report (from memory if available, otherwise disk).
    /// </summary>
    /// <returns>The latest report, or an empty report if none exists.</returns>
    public GapReport Load()
    {
        lock (_lock)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            try
            {
                var dataFolder = DataFolder;

                // Flush always writes the meta file, on every save, unconditionally - it existing at all
                // already means this install has converted, whether or not any domain currently has gaps
                // (an empty domain's file is deleted, not left behind empty), so its presence alone answers
                // the question; checking the domain files too would only repeat what it already confirms.
                if (File.Exists(MetaFilePath(dataFolder)))
                {
                    _cached = LoadSplit(dataFolder);
                }
                else
                {
                    var legacy = LegacyFilePath(dataFolder);
                    if (File.Exists(legacy))
                    {
                        _cached = JsonSerializer.Deserialize<GapReport>(File.ReadAllText(legacy), _jsonOptions);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read gap report");
            }

            return _cached ?? new GapReport { GeneratedUtc = DateTime.MinValue };
        }
    }

    // Reassembles one unified report from the per-domain files plus the shared meta file; the very next
    // Flush (from any save path) writes the split files going forward, so this only ever runs once per
    // process against a legacy-format install, or after a legacy import.
    private static GapReport LoadSplit(string dataFolder)
    {
        var meta = new GapReport();
        var metaPath = MetaFilePath(dataFolder);
        if (File.Exists(metaPath))
        {
            meta = JsonSerializer.Deserialize<GapReport>(File.ReadAllText(metaPath), _jsonOptions) ?? new GapReport();
        }

        var items = new List<GapItem>();
        foreach (var domain in MediaDomains.Implemented)
        {
            var path = DomainFilePath(dataFolder, domain);
            if (!File.Exists(path))
            {
                continue;
            }

            var domainItems = JsonSerializer.Deserialize<List<GapItem>>(File.ReadAllText(path), _jsonOptions);
            if (domainItems is not null)
            {
                items.AddRange(domainItems);
            }
        }

        return new GapReport
        {
            GeneratedUtc = meta.GeneratedUtc,
            GeneratedVersion = meta.GeneratedVersion,
            TotalGaps = meta.TotalGaps,
            Items = items,
            SourceRuns = meta.SourceRuns
        };
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
            var report = Load();
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
            var report = Load();
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
            var report = Load();
            if (_summaryFacts is null || !ReferenceEquals(_summaryFactsSource, report))
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
                _summaryFactsSource = report;
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
            return Load().Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));
        }
    }
}
