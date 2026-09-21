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
/// Persists each user's own todo list (gaps they chose to acquire) to the plugin data folder, one file per
/// user under <c>watchlists/</c>, keyed by <see cref="GapItem.Id"/>, so a list survives rescans even when the
/// report does not carry a gap. A user's file is deleted when the user is (see <see cref="TodoOwner"/>).
/// </summary>
/// <remarks>
/// The lists live in the plugin's own data folder, not the server's cache path: the server's daily cache
/// sweep deletes files that have not been written for thirty days, which would delete a list nobody edited.
/// </remarks>
public sealed class TodoStore
{
    private const int MaxIdLength = 256;
    private const string LegacyFileName = "todos.json";
    private const string ListsFolderName = "watchlists";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<TodoStore> _logger;
    private readonly string? _dataFolderOverride;
    private readonly object _lock = new();
    private readonly Dictionary<Guid, Dictionary<string, TodoEntry>> _cached = [];
    private bool _legacyResolved;

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
    /// <param name="dataFolder">The folder to persist the todo lists in.</param>
    public TodoStore(ILogger<TodoStore> logger, string dataFolder)
    {
        _logger = logger;
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

    private string ListsFolder => Path.Combine(DataFolder, ListsFolderName);

    /// <summary>
    /// Snapshots the given gaps and upserts them into a user's todo list, keyed by id. Re-adding a gap already
    /// on the list refreshes its snapshot but preserves the existing entry's done state and timestamps (its
    /// <see cref="TodoEntry.Done"/>, <see cref="TodoEntry.DoneUtc"/>, and <see cref="TodoEntry.AddedUtc"/>),
    /// so a user does not lose progress by adding the same title twice.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <param name="gaps">The gaps to snapshot and add.</param>
    /// <returns>The number of entries that were newly added (not already on the list).</returns>
    public int Add(Guid userId, IReadOnlyList<GapItem> gaps)
    {
        RequireUser(userId);
        ArgumentNullException.ThrowIfNull(gaps);

        lock (_lock)
        {
            var map = LoadMap(userId);
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
                Flush(userId, map);
            }

            return added;
        }
    }

    /// <summary>
    /// Removes an entry from a user's todo list.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <param name="id">The entry id.</param>
    /// <returns>The number of entries removed (0 or 1).</returns>
    public int Remove(Guid userId, string id)
    {
        RequireUser(userId);
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength)
        {
            return 0;
        }

        lock (_lock)
        {
            var map = LoadMap(userId);
            if (map.Remove(id))
            {
                Flush(userId, map);
                return 1;
            }

            return 0;
        }
    }

    /// <summary>
    /// Gets a copy of every entry on a user's todo list, in no particular order.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <returns>The entries.</returns>
    public IReadOnlyList<TodoEntry> Load(Guid userId)
    {
        RequireUser(userId);

        lock (_lock)
        {
            // The list copy decouples callers from the live cached map (a later Add/Remove must not mutate it).
            return new List<TodoEntry>(LoadMap(userId).Values);
        }
    }

    /// <summary>
    /// Gets the identity keys of every title on a user's list (see <see cref="TodoKeys"/>), for asking whether a
    /// card's title is on it without reading the list once per card.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <returns>The keys.</returns>
    public IReadOnlySet<string> WantedKeys(Guid userId)
    {
        RequireUser(userId);

        lock (_lock)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in LoadMap(userId).Values)
            {
                keys.UnionWith(TodoKeys.For(entry));
            }

            return keys;
        }
    }

    /// <summary>
    /// Removes every entry on a user's list that is about one of the given titles.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <param name="keys">The identity keys of the title (see <see cref="GapTargetKey"/>).</param>
    /// <returns>The number of entries removed.</returns>
    public int RemoveMatching(Guid userId, IReadOnlyCollection<string> keys)
    {
        RequireUser(userId);
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return 0;
        }

        lock (_lock)
        {
            var map = LoadMap(userId);
            var gone = map.Where(pair => TodoKeys.For(pair.Value).Any(keys.Contains)).Select(pair => pair.Key).ToList();
            foreach (var id in gone)
            {
                map.Remove(id);
            }

            if (gone.Count > 0)
            {
                Flush(userId, map);
            }

            return gone.Count;
        }
    }

    /// <summary>
    /// Lists the users that have at least one entry on their todo list.
    /// </summary>
    /// <returns>The users' ids, in no particular order.</returns>
    public IReadOnlyList<Guid> ListOwners()
    {
        lock (_lock)
        {
            var owners = new List<Guid>();
            var folder = ListsFolder;
            if (!Directory.Exists(folder))
            {
                return owners;
            }

            foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
            {
                if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var userId)
                    && userId != Guid.Empty
                    && LoadMap(userId).Count > 0)
                {
                    owners.Add(userId);
                }
            }

            return owners;
        }
    }

    /// <summary>
    /// Sets an entry's done state, stamping or clearing its done timestamp.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <param name="id">The entry id.</param>
    /// <param name="done">Whether the entry is done.</param>
    /// <returns><see langword="true"/> if the entry exists and was updated.</returns>
    public bool SetDone(Guid userId, string id, bool done)
    {
        RequireUser(userId);
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength)
        {
            return false;
        }

        lock (_lock)
        {
            var map = LoadMap(userId);
            if (!map.TryGetValue(id, out var entry))
            {
                return false;
            }

            entry.Done = done;
            entry.DoneUtc = done ? NowUtc() : null;
            Flush(userId, map);
            return true;
        }
    }

    /// <summary>
    /// Applies a done state to many entries at once, under one lock and with a single flush. The per-entry
    /// <see cref="SetDone"/> serializes the whole file each call, so a bulk verify that touched N entries
    /// would otherwise write the file N times.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <param name="states">The wanted done state per entry id. Ids the list does not hold are ignored.</param>
    /// <returns>The number of entries whose state actually changed.</returns>
    public int ReconcileDone(Guid userId, IReadOnlyDictionary<string, bool> states)
    {
        RequireUser(userId);
        ArgumentNullException.ThrowIfNull(states);

        lock (_lock)
        {
            var map = LoadMap(userId);
            var changed = 0;
            foreach (var pair in states)
            {
                if (!map.TryGetValue(pair.Key, out var entry) || entry.Done == pair.Value)
                {
                    continue;
                }

                entry.Done = pair.Value;
                entry.DoneUtc = pair.Value ? NowUtc() : null;
                changed++;
            }

            // Only write when something moved, so a verify that confirms the status quo costs no disk at all.
            if (changed > 0)
            {
                Flush(userId, map);
            }

            return changed;
        }
    }

    /// <summary>
    /// Moves the server-wide todo list (<c>todos.json</c>) into an administrator's own list, once, and deletes
    /// it. Entries the administrator already has are kept as they are. The file is deleted only after the
    /// administrator's list has been written, so a failed write leaves it to be adopted on the next call, and a
    /// file that cannot be read is left in place.
    /// </summary>
    /// <param name="administratorId">The administrator whose list receives the entries.</param>
    /// <returns>The number of entries moved.</returns>
    public int AdoptLegacy(Guid administratorId)
    {
        RequireUser(administratorId);

        lock (_lock)
        {
            if (_legacyResolved)
            {
                return 0;
            }

            var legacyPath = Path.Combine(DataFolder, LegacyFileName);
            if (!File.Exists(legacyPath))
            {
                _legacyResolved = true;
                return 0;
            }

            Dictionary<string, TodoEntry>? legacy;
            try
            {
                legacy = JsonSerializer.Deserialize<Dictionary<string, TodoEntry>>(File.ReadAllText(legacyPath), _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The server-wide todo list could not be read; it is left in place");
                _legacyResolved = true;
                return 0;
            }

            var merged = new Dictionary<string, TodoEntry>(LoadMap(administratorId), StringComparer.Ordinal);
            var adopted = 0;
            foreach (var pair in legacy ?? [])
            {
                if (merged.TryAdd(pair.Key, pair.Value))
                {
                    adopted++;
                }
            }

            if (!Write(administratorId, merged))
            {
                return 0;
            }

            _cached[administratorId] = merged;
            _legacyResolved = true;
            try
            {
                File.Delete(legacyPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The server-wide todo list was adopted but the old file could not be deleted");
            }

            _logger.LogInformation("Moved {Count} entries of the server-wide todo list to administrator {User}'s own list", adopted, administratorId);
            return adopted;
        }
    }

    /// <summary>
    /// Deletes a user's todo list.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <returns><see langword="true"/> when a list file existed and was removed.</returns>
    public bool Delete(Guid userId)
    {
        RequireUser(userId);

        lock (_lock)
        {
            _cached.Remove(userId);
            var path = PathFor(userId);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete the todo list of user {User}", userId);
                return false;
            }
        }
    }

    /// <summary>
    /// Deletes the todo list of every user that does not exist, for lists left behind when a user was deleted
    /// while the plugin was not listening. A file whose name is not a user id is left alone.
    /// </summary>
    /// <param name="userExists">Whether a user with this id exists.</param>
    /// <returns>The number of lists deleted.</returns>
    public int Prune(Func<Guid, bool> userExists)
    {
        ArgumentNullException.ThrowIfNull(userExists);

        lock (_lock)
        {
            var folder = ListsFolder;
            if (!Directory.Exists(folder))
            {
                return 0;
            }

            var deleted = 0;
            foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
            {
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var userId) || userId == Guid.Empty || userExists(userId))
                {
                    continue;
                }

                _cached.Remove(userId);
                try
                {
                    File.Delete(path);
                    deleted++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete the orphaned todo list {Path}", path);
                }
            }

            return deleted;
        }
    }

    private static void RequireUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A todo list belongs to a user.", nameof(userId));
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
            ImageUrl = gap.ImageUrl,
            ReleaseDate = gap.ReleaseDate,
            ProviderIds = new Dictionary<string, string>(gap.ProviderIds, StringComparer.OrdinalIgnoreCase),
            Links = new List<ExternalLink>(gap.Links),
            AddedUtc = NowUtc()
        };

    private string PathFor(Guid userId)
        => Path.Combine(ListsFolder, userId.ToString("N", CultureInfo.InvariantCulture) + ".json");

    // Caller holds _lock.
    private Dictionary<string, TodoEntry> LoadMap(Guid userId)
    {
        if (_cached.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        Dictionary<string, TodoEntry>? map = null;
        try
        {
            var path = PathFor(userId);
            if (File.Exists(path))
            {
                map = JsonSerializer.Deserialize<Dictionary<string, TodoEntry>>(File.ReadAllText(path), _jsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the todo list of user {User}", userId);
        }

        map ??= new Dictionary<string, TodoEntry>(StringComparer.Ordinal);
        _cached[userId] = map;
        return map;
    }

    // Keeps the in-memory list and writes it; caller holds _lock.
    private void Flush(Guid userId, Dictionary<string, TodoEntry> map)
    {
        _cached[userId] = map;
        Write(userId, map);
    }

    // Atomic write (temp then replace); caller holds _lock.
    private bool Write(Guid userId, Dictionary<string, TodoEntry> map)
    {
        try
        {
            Directory.CreateDirectory(ListsFolder);
            var path = PathFor(userId);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(map, _jsonOptions));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist the todo list of user {User}", userId);
            return false;
        }
    }
}
