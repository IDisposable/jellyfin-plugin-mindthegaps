using System;
using System.IO;
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

    private static string FilePath
    {
        get
        {
            var dataFolder = Plugin.Instance?.DataFolderPath ?? Path.GetTempPath();
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
    /// Caches the report in memory and flushes to disk only if enough time has passed since the last
    /// flush (the in-memory copy is always current). For frequent checkpoint saves during a long pass,
    /// so a large report is not fully rewritten on every checkpoint. Call <see cref="Save"/> for the
    /// final write.
    /// </summary>
    /// <param name="report">The report to save.</param>
    public void SaveThrottled(GapReport report)
    {
        lock (_lock)
        {
            _cached = report;
            if (DateTime.UtcNow - _lastWriteUtc >= _minWriteInterval)
            {
                Flush(report);
            }
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
}
