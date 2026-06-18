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
}
