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

    private readonly ILogger<GapStore> _logger;
    private readonly object _lock = new();
    private GapReport? _cached;

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
    /// Saves the report to disk and caches it in memory.
    /// </summary>
    /// <param name="report">The report to save.</param>
    public void Save(GapReport report)
    {
        lock (_lock)
        {
            _cached = report;
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(report, _jsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist gap report");
            }
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
