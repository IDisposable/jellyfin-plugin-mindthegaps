using System;
using System.Collections.Generic;
using System.Globalization;
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

    private readonly ILogger<GapStore> _logger;
    private readonly string? _dataFolderOverride;
    private readonly object _lock = new();
    private GapReport? _cached;
    private DateTime _lastWriteUtc = DateTime.MinValue;

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

    private string FilePath
    {
        get
        {
            var dataFolder = _dataFolderOverride ?? Plugin.Instance?.DataFolderPath ?? Path.GetTempPath();
            Directory.CreateDirectory(dataFolder);
            return Path.Combine(dataFolder, "gaps.json");
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
            _cached = report;
            Flush(report);
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
            Flush(report);
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
                Flush(current);
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
                Items = items
            };
            _cached = report;
            Flush(report);
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
            var removed = 0;
            foreach (var item in current.Items)
            {
                if (item.Adhoc
                    && (sourceItemId is null || string.Equals(item.SourceItemId, sourceItemId, StringComparison.Ordinal)))
                {
                    removed++;
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
                Items = kept
            };
            _cached = report;
            Flush(report);
            return removed;
        }
    }

    /// <summary>
    /// Replaces a single series' content gaps with a fresh re-check and saves, leaving every other gap
    /// untouched. Used by the per-series re-check so a metadata fix can be verified without a full rescan;
    /// unlike an additive merge, this also drops gaps the fix resolved. The report's scan time and version
    /// are preserved (a re-check is a partial update, not a new scan).
    /// </summary>
    /// <param name="seriesId">The owned series whose episode gaps are being replaced.</param>
    /// <param name="recheck">The freshly computed gaps for that series.</param>
    /// <returns>The updated report.</returns>
    public GapReport ReplaceSeriesGaps(Guid seriesId, GapReport recheck)
    {
        ArgumentNullException.ThrowIfNull(recheck);

        var seriesKey = seriesId.ToString("N", CultureInfo.InvariantCulture);

        lock (_lock)
        {
            var current = Load();
            var kept = new List<GapItem>(current.Items.Count + recheck.Items.Count);
            foreach (var item in current.Items)
            {
                // A series' content gaps all carry the series as their source and a "seriescontent:" id; drop
                // exactly those (the prefix avoids touching a recommendation the series happens to seed).
                var isSeriesContent = string.Equals(item.SourceItemId, seriesKey, StringComparison.Ordinal)
                    && item.Id.StartsWith("seriescontent:", StringComparison.Ordinal);
                if (!isSeriesContent)
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
                Items = kept
            };
            _cached = report;
            Flush(report);
            return report;
        }
    }

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

    // Atomic write: serialize to a temp file then replace, so a crash mid-write cannot truncate or lose
    // the report. Caller holds _lock.
    private void Flush(GapReport report)
    {
        try
        {
            var path = FilePath;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(report, _jsonOptions));
            File.Move(tmp, path, overwrite: true);
            _lastWriteUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist gap report");
        }
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
                var path = FilePath;
                if (File.Exists(path))
                {
                    _cached = JsonSerializer.Deserialize<GapReport>(File.ReadAllText(path), _jsonOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read gap report");
            }

            return _cached ?? new GapReport { GeneratedUtc = DateTime.MinValue };
        }
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
                Items = report.Items.ToArray()
            };
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
