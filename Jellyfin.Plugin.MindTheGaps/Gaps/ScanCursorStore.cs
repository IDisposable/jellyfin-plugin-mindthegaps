using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Persists, per gap source, when each library item was last scanned (a side table keyed by the item's
/// guid, so the owned items are never touched). A capped source uses it as a staleness queue: each run it
/// scans the items that were scanned longest ago, with never-scanned items first, so over repeated runs
/// the whole library is covered and then the stalest entries refresh, with no explicit cycle reset.
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
    private Dictionary<string, Dictionary<string, DateTime>>? _cache;

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
    /// Gets, for a source, the last-scanned UTC time of each item it has scanned. Items absent from the
    /// map have never been scanned and rank as the stalest.
    /// </summary>
    /// <param name="source">The source name.</param>
    /// <returns>The item-key to last-scanned-time map (a snapshot copy).</returns>
    public IReadOnlyDictionary<string, DateTime> GetLastScanned(string source)
    {
        lock (_lock)
        {
            var map = Load();
            return map.TryGetValue(source, out var times)
                ? new Dictionary<string, DateTime>(times, StringComparer.Ordinal)
                : new Dictionary<string, DateTime>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Drops stored entries for a source whose item key is not in the current live set, so the table
    /// stays the size of the library and a deleted item cannot leave a stale stamp behind (which would
    /// matter if its deterministic guid were later reused by a re-added item). A no-op when nothing is
    /// pruned, so no needless write.
    /// </summary>
    /// <param name="source">The source name.</param>
    /// <param name="liveKeys">The keys of every candidate item the source currently sees.</param>
    public void RetainOnly(string source, IReadOnlyCollection<string> liveKeys)
    {
        var live = liveKeys as ISet<string> ?? new HashSet<string>(liveKeys, StringComparer.Ordinal);
        lock (_lock)
        {
            var map = Load();
            if (!map.TryGetValue(source, out var times))
            {
                return;
            }

            var dead = new List<string>();
            foreach (var key in times.Keys)
            {
                if (!live.Contains(key))
                {
                    dead.Add(key);
                }
            }

            if (dead.Count == 0)
            {
                return;
            }

            foreach (var key in dead)
            {
                times.Remove(key);
            }

            Flush(map);
        }
    }

    /// <summary>
    /// Clears all scan-rotation state, so every source treats every item as never-scanned and a fresh
    /// coverage cycle starts on the next scan.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            var map = Load();
            if (map.Count == 0)
            {
                return;
            }

            map.Clear();
            Flush(map);
        }
    }

    /// <summary>
    /// Stamps the given item keys as scanned now for a source.
    /// </summary>
    /// <param name="source">The source name.</param>
    /// <param name="keys">The item keys just scanned.</param>
    public void MarkScanned(string source, IReadOnlyCollection<string> keys)
    {
        if (keys.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        lock (_lock)
        {
            var map = Load();
            if (!map.TryGetValue(source, out var times))
            {
                times = new Dictionary<string, DateTime>(StringComparer.Ordinal);
                map[source] = times;
            }

            foreach (var key in keys)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    times[key] = now;
                }
            }

            Flush(map);
        }
    }

    private Dictionary<string, Dictionary<string, DateTime>> Load()
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
                _cache = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, DateTime>>>(File.ReadAllText(path), _jsonOptions);
            }
        }
        catch (Exception ex)
        {
            // A pre-existing file in the old (set) format simply will not parse; treat it as empty and
            // rebuild, which costs one fresh coverage cycle and nothing more.
            _logger.LogWarning(ex, "Could not read scan cursors; starting fresh");
        }

        return _cache ??= new Dictionary<string, Dictionary<string, DateTime>>(StringComparer.Ordinal);
    }

    // Atomic write: serialize to a temp file then replace. Caller holds _lock.
    private void Flush(Dictionary<string, Dictionary<string, DateTime>> map)
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
