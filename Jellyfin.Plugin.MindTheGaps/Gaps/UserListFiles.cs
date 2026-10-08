using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// The file handling a per-user list shares: one JSON map per user in its own folder under the plugin data
/// folder, named by the user's id, read once and kept in memory, written atomically. Holds no lock of its
/// own; the store that owns it serializes every call under its lock.
/// </summary>
/// <remarks>
/// The lists live in the plugin's own data folder, not the server's cache path: the server's daily cache
/// sweep deletes files that have not been written for thirty days, which would delete a list nobody edited.
/// </remarks>
/// <typeparam name="TEntry">The entry type, keyed by its own id.</typeparam>
internal sealed class UserListFiles<TEntry>
{
    private static readonly JsonSerializerOptions _jsonOptions = StoredJson.Create();

    private readonly ILogger _logger;
    private readonly Func<string> _dataFolder;
    private readonly string _folderName;
    private readonly string _noun;
    private readonly Action<TEntry>? _afterRead;
    private readonly Dictionary<Guid, Dictionary<string, TEntry>> _cached = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="UserListFiles{TEntry}"/> class.
    /// </summary>
    /// <param name="logger">The owning store's logger.</param>
    /// <param name="dataFolder">The plugin data folder, read on each use since the plugin sets it after
    /// construction.</param>
    /// <param name="folderName">The folder the lists live in, under the data folder.</param>
    /// <param name="noun">What a list is called in the log ("todo list").</param>
    /// <param name="afterRead">Rebuilds what an entry does not keep on disk, run on each entry read from a file.</param>
    public UserListFiles(ILogger logger, Func<string> dataFolder, string folderName, string noun, Action<TEntry>? afterRead = null)
    {
        _afterRead = afterRead;
        _logger = logger;
        _dataFolder = dataFolder;
        _folderName = folderName;
        _noun = noun;
    }

    /// <summary>
    /// Gets the folder the lists live in.
    /// </summary>
    public string Folder => Path.Combine(_dataFolder(), _folderName);

    /// <summary>
    /// Gets the options the lists are read and written with, for a store reading a file of the same shape.
    /// </summary>
    public static JsonSerializerOptions JsonOptions => _jsonOptions;

    /// <summary>
    /// Gets a user's list to change it, read from disk the first time and kept. The live map: the caller copies
    /// it before handing it out.
    /// </summary>
    /// <remarks>
    /// A list that cannot be read is never treated as empty, since the next write would replace the real one
    /// with whatever the change added to nothing. A read that fails (an I/O error, a permission, a lock) throws
    /// and caches nothing, so the next call reads again. A file that reads but does not parse will not get
    /// better by asking again, so it is moved aside, kept beside the list for recovery, and the list starts
    /// empty.
    /// </remarks>
    /// <param name="userId">The list's owner.</param>
    /// <returns>The list, empty when the user has none.</returns>
    /// <exception cref="UserListUnavailableException">The list exists but could not be read just now.</exception>
    public Dictionary<string, TEntry> Load(Guid userId)
    {
        if (_cached.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var path = PathFor(userId);
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return Keep(userId, null);
            }

            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the {Noun} of user {User}; it is left as it is and read again on next use", _noun, userId);
            throw new UserListUnavailableException("The " + _noun + " could not be read.", ex);
        }

        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, TEntry>>(text, _jsonOptions);
            if (read is not null && _afterRead is not null)
            {
                foreach (var entry in read.Values)
                {
                    _afterRead(entry);
                }
            }

            return Keep(userId, read);
        }
        catch (JsonException ex)
        {
            SetAside(userId, path, ex);
            return Keep(userId, null);
        }
    }

    /// <summary>
    /// Gets a user's list to read it, when an answer is better than an error: as <see cref="Load"/>, except that
    /// a list that cannot be read just now reads as empty for this call only, cached nowhere and written nowhere.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <returns>The list, empty when the user has none or it cannot be read just now.</returns>
    public Dictionary<string, TEntry> Peek(Guid userId)
    {
        try
        {
            return Load(userId);
        }
        catch (UserListUnavailableException)
        {
            return new Dictionary<string, TEntry>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Writes a user's list (temp file, then replace) and keeps it in memory once it is on disk.
    /// </summary>
    /// <remarks>
    /// A write that fails drops the list from memory as well as throwing. A caller changes the map
    /// <see cref="Load"/> handed it in place, so keeping it would leave in memory a change that is not on disk:
    /// the page would show it until a restart quietly lost it. Dropped, the next use reads what is really there.
    /// </remarks>
    /// <param name="userId">The list's owner.</param>
    /// <param name="map">The list.</param>
    /// <exception cref="UserListUnavailableException">The list could not be written.</exception>
    public void Save(Guid userId, Dictionary<string, TEntry> map)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = PathFor(userId);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(map, _jsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _cached.Remove(userId);
            _logger.LogError(ex, "Could not save the {Noun} of user {User}; the change was not made", _noun, userId);
            throw new UserListUnavailableException("The " + _noun + " could not be saved.", ex);
        }

        _cached[userId] = map;
    }

    /// <summary>
    /// Lists the users that have a list file, whatever it holds.
    /// </summary>
    /// <returns>The users' ids, in no particular order.</returns>
    public IEnumerable<Guid> Owners()
    {
        var folder = Folder;
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
        {
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var userId) && userId != Guid.Empty)
            {
                yield return userId;
            }
        }
    }

    /// <summary>
    /// Deletes a user's list, along with any copy of it set aside as unreadable and any write left unfinished.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <returns><see langword="true"/> when a file of theirs existed and was removed.</returns>
    public bool Delete(Guid userId)
    {
        _cached.Remove(userId);
        var deleted = false;
        foreach (var path in FilesOf(userId))
        {
            try
            {
                File.Delete(path);
                deleted = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete {Path}, part of the {Noun} of user {User}", path, _noun, userId);
            }
        }

        return deleted;
    }

    /// <summary>
    /// Deletes the files of every user that does not exist. A file whose name does not start with a user id is
    /// left alone.
    /// </summary>
    /// <param name="userExists">Whether a user with this id exists.</param>
    /// <returns>The number of users whose files were deleted.</returns>
    public int Prune(Func<Guid, bool> userExists)
    {
        ArgumentNullException.ThrowIfNull(userExists);

        var folder = Folder;
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        var users = new HashSet<Guid>();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(path);
            var dot = name.IndexOf('.', StringComparison.Ordinal);
            if (dot > 0 && Guid.TryParseExact(name[..dot], "N", out var userId) && userId != Guid.Empty)
            {
                users.Add(userId);
            }
        }

        return users.Count(userId => !userExists(userId) && Delete(userId));
    }

    private Dictionary<string, TEntry> Keep(Guid userId, Dictionary<string, TEntry>? map)
    {
        // A file holding a bare null parses to nothing; an empty list is the same thing.
        map ??= new Dictionary<string, TEntry>(StringComparer.Ordinal);
        _cached[userId] = map;
        return map;
    }

    // Moves a file that does not parse out of the way, so the list can be used again without writing over it.
    private void SetAside(Guid userId, string path, JsonException parseError)
    {
        var keptAs = path + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        try
        {
            File.Move(path, keptAs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The {Noun} of user {User} does not parse and could not be set aside; it is left as it is", _noun, userId);
            throw new UserListUnavailableException("The " + _noun + " does not parse and could not be set aside.", ex);
        }

        _logger.LogError(parseError, "The {Noun} of user {User} does not parse; it was kept as {Path} and the list starts empty", _noun, userId, keptAs);
    }

    // The list, a copy set aside as unreadable, and a temp file a write did not finish: all named for the user.
    private List<string> FilesOf(Guid userId)
    {
        var folder = Folder;
        var prefix = Path.GetFileName(PathFor(userId));
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, prefix + "*").ToList() : [];
    }

    private string PathFor(Guid userId)
        => Path.Combine(Folder, userId.ToString("N", CultureInfo.InvariantCulture) + ".json");
}
