using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// How long each gap source took on its last complete run, by source name, so the next scan can weigh its
/// progress bar by what each source actually costs on this library (<see cref="ScanProgressEstimate"/>).
/// Persisted to <c>scan-durations.json</c> in the plugin data folder; losing it costs one unweighted bar.
/// </summary>
public sealed class SourceDurationStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<SourceDurationStore> _logger;
    private readonly string? _dataFolderOverride;
    private readonly object _lock = new();
    private Dictionary<string, double>? _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceDurationStore"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public SourceDurationStore(ILogger<SourceDurationStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceDurationStore"/> class with an explicit data folder.
    /// Test seam.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="dataFolder">The folder to persist into.</param>
    public SourceDurationStore(ILogger<SourceDurationStore> logger, string dataFolder)
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
            return Path.Combine(dataFolder, "scan-durations.json");
        }
    }

    /// <summary>
    /// Gets each named source's last run in seconds, null for one with none recorded.
    /// </summary>
    /// <param name="sources">The source names, in the order the caller wants them back.</param>
    /// <returns>The seconds per source, in the same order.</returns>
    public IReadOnlyList<double?> Get(IReadOnlyList<string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        lock (_lock)
        {
            var map = Load();
            var result = new double?[sources.Count];
            for (var i = 0; i < sources.Count; i++)
            {
                result[i] = map.TryGetValue(sources[i], out var seconds) ? seconds : null;
            }

            return result;
        }
    }

    /// <summary>
    /// Records the runs of the sources that completed, replacing what each had before. A source that failed
    /// or was cancelled stopped early, so it is left out rather than recorded as fast.
    /// </summary>
    /// <param name="seconds">The seconds per completed source, by name.</param>
    public void Record(IReadOnlyDictionary<string, double> seconds)
    {
        ArgumentNullException.ThrowIfNull(seconds);
        if (seconds.Count == 0)
        {
            return;
        }

        lock (_lock)
        {
            var map = Load();
            foreach (var (name, value) in seconds)
            {
                map[name] = Math.Round(value, 1);
            }

            Flush(map);
        }
    }

    private Dictionary<string, double> Load()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        try
        {
            var path = FilePath;
            if (File.Exists(path))
            {
                var stored = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(path), _jsonOptions);
                if (stored is not null)
                {
                    _cache = new Dictionary<string, double>(stored, StringComparer.Ordinal);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read scan durations; the next scan's progress is unweighted");
        }

        return _cache ??= new Dictionary<string, double>(StringComparer.Ordinal);
    }

    // Atomic write: serialize to a temp file then replace. Caller holds _lock.
    private void Flush(Dictionary<string, double> map)
    {
        try
        {
            var path = FilePath;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(map, _jsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist scan durations");
        }
    }
}
