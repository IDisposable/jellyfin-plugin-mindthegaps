using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Persists the user's personal todo list (gaps they chose to acquire) to the plugin data folder beside
/// the report and resolutions, keyed by <see cref="GapItem.Id"/>, so it survives rescans even when the
/// report no longer carries a gap.
/// </summary>
public sealed class TodoStore
{
    private const int MaxIdLength = 256;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<TodoStore> _logger;
    private readonly string? _dataFolderOverride;
    private readonly object _lock = new();
    private Dictionary<string, TodoEntry>? _cached;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoStore"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public TodoStore(ILogger<TodoStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoStore"/> class persisting to a specific data folder
    /// (instead of the plugin's). Used by tests.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="dataFolder">The folder to persist the todo list in.</param>
    public TodoStore(ILogger<TodoStore> logger, string dataFolder)
    {
        _logger = logger;
        _dataFolderOverride = dataFolder;
    }

    private string FilePath
    {
        get
        {
            var dataFolder = _dataFolderOverride ?? Plugin.Instance?.DataFolderPath ?? Path.GetTempPath();
            Directory.CreateDirectory(dataFolder);
            return Path.Combine(dataFolder, "todos.json");
        }
    }

    /// <summary>
    /// Snapshots the given gaps and upserts them into the todo list, keyed by id. Re-adding a gap already on
    /// the list refreshes its snapshot but preserves the existing entry's done state and timestamps (its
    /// <see cref="TodoEntry.Done"/>, <see cref="TodoEntry.DoneUtc"/>, and <see cref="TodoEntry.AddedUtc"/>),
    /// so a user does not lose progress by adding the same title twice.
    /// </summary>
    /// <param name="gaps">The gaps to snapshot and add.</param>
    /// <returns>The number of entries that were newly added (not already on the list).</returns>
    public int Add(IReadOnlyList<GapItem> gaps)
    {
        ArgumentNullException.ThrowIfNull(gaps);

        lock (_lock)
        {
            var map = LoadMap();
            var added = 0;
            var flushed = false;
            foreach (var gap in gaps)
            {
                if (gap is null || string.IsNullOrEmpty(gap.Id) || gap.Id.Length > MaxIdLength)
                {
                    continue;
                }

                var entry = Snapshot(gap);
                if (map.TryGetValue(gap.Id, out var existing))
                {
                    // Re-add: keep the user's progress (done state and the original added timestamp).
                    entry.Done = existing.Done;
                    entry.DoneUtc = existing.DoneUtc;
                    entry.AddedUtc = existing.AddedUtc;
                }
                else
                {
                    added++;
                }

                map[gap.Id] = entry;
                flushed = true;
            }

            if (flushed)
            {
                Flush(map);
            }

            return added;
        }
    }

    /// <summary>
    /// Removes an entry from the todo list.
    /// </summary>
    /// <param name="id">The entry id.</param>
    /// <returns>The number of entries removed (0 or 1).</returns>
    public int Remove(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength)
        {
            return 0;
        }

        lock (_lock)
        {
            var map = LoadMap();
            if (map.Remove(id))
            {
                Flush(map);
                return 1;
            }

            return 0;
        }
    }

    /// <summary>
    /// Gets a copy of every todo entry, in no particular order.
    /// </summary>
    /// <returns>The entries.</returns>
    public IReadOnlyList<TodoEntry> Load()
    {
        lock (_lock)
        {
            // The list copy decouples callers from the live cached map (a later Add/Remove must not mutate it).
            return new List<TodoEntry>(LoadMap().Values);
        }
    }

    /// <summary>
    /// Sets an entry's done state, stamping or clearing its done timestamp.
    /// </summary>
    /// <param name="id">The entry id.</param>
    /// <param name="done">Whether the entry is done.</param>
    /// <returns><see langword="true"/> if the entry exists and was updated.</returns>
    public bool SetDone(string id, bool done)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength)
        {
            return false;
        }

        lock (_lock)
        {
            var map = LoadMap();
            if (!map.TryGetValue(id, out var entry))
            {
                return false;
            }

            entry.Done = done;
            entry.DoneUtc = done ? NowUtc() : null;
            Flush(map);
            return true;
        }
    }

    // A round-trippable UTC instant for the added/done timestamps.
    private static string NowUtc()
        => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

    // Capture just enough of a gap to render, link, and verify the entry later. The creator is the gap's
    // owning source item name (the author/artist/creator-or-source that surfaced it).
    private static TodoEntry Snapshot(GapItem gap)
        => new()
        {
            Id = gap.Id,
            Name = gap.Name,
            Year = gap.Year,
            DomainName = gap.DomainName,
            TargetKindName = gap.TargetKindName,
            PatternName = gap.PatternName,
            Creator = gap.SourceItemName,
            ProviderIds = new Dictionary<string, string>(gap.ProviderIds, StringComparer.OrdinalIgnoreCase),
            Links = new List<ExternalLink>(gap.Links),
            AddedUtc = NowUtc()
        };

    // Caller holds _lock.
    private Dictionary<string, TodoEntry> LoadMap()
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
                _cached = JsonSerializer.Deserialize<Dictionary<string, TodoEntry>>(File.ReadAllText(path), _jsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the todo list");
        }

        return _cached ??= new Dictionary<string, TodoEntry>(StringComparer.Ordinal);
    }

    // Atomic write (temp then replace); caller holds _lock.
    private void Flush(Dictionary<string, TodoEntry> map)
    {
        _cached = map;
        try
        {
            var path = FilePath;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(map, _jsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist the todo list");
        }
    }
}
