using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Persists, per gap source, the set of items already scanned in the current cycle, so a source that
/// caps its work per run can advance to the next batch next run instead of rescanning the same items.
/// Paired with the engine carrying unowned gaps forward across scans, this lets a large library "fill
/// up" over repeated runs. When everything has been scanned the source starts a fresh cycle.
/// </summary>
public sealed class ScanCursorStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<ScanCursorStore> _logger;
    private readonly string? _dataFolderOverride;
    private readonly object _lock = new();
    private Dictionary<string, HashSet<string>>? _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScanCursorStore"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public ScanCursorStore(ILogger<ScanCursorStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScanCursorStore"/> class with an explicit data folder.
    /// Test seam.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="dataFolder">The folder to persist into.</param>
    public ScanCursorStore(ILogger<ScanCursorStore> logger, string dataFolder)
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
            return Path.Combine(dataFolder, "scan-cursors.json");
        }
    }

    /// <summary>
    /// Gets the keys already scanned in the current cycle for a source.
    /// </summary>
    /// <param name="source">The source name.</param>
    /// <returns>The processed keys.</returns>
    public IReadOnlyCollection<string> GetProcessed(string source)
    {
        lock (_lock)
        {
            var map = Load();
            return map.TryGetValue(source, out var set) ? set.ToArray() : Array.Empty<string>();
        }
    }

    /// <summary>
    /// Adds keys to a source's processed set for the current cycle.
    /// </summary>
    /// <param name="source">The source name.</param>
    /// <param name="keys">The keys just scanned.</param>
    public void MarkProcessed(string source, IReadOnlyCollection<string> keys)
    {
        if (keys.Count == 0)
        {
            return;
        }

        lock (_lock)
        {
            var map = Load();
            if (!map.TryGetValue(source, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[source] = set;
            }

            foreach (var key in keys)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    set.Add(key);
                }
            }

            Flush(map);
        }
    }

    /// <summary>
    /// Clears a source's processed set, starting a fresh coverage cycle.
    /// </summary>
    /// <param name="source">The source name.</param>
    public void StartNewCycle(string source)
    {
        lock (_lock)
        {
            var map = Load();
            if (map.Remove(source))
            {
                Flush(map);
            }
        }
    }

    private Dictionary<string, HashSet<string>> Load()
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
                var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path), _jsonOptions);
                if (raw is not null)
                {
                    _cache = raw.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new HashSet<string>(kvp.Value, StringComparer.Ordinal),
                        StringComparer.Ordinal);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read scan cursors");
        }

        return _cache ??= new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    }

    // Atomic write: serialize to a temp file then replace. Caller holds _lock.
    private void Flush(Dictionary<string, HashSet<string>> map)
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
            _logger.LogError(ex, "Failed to persist scan cursors");
        }
    }
}
