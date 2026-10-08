using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Persists each user's own list of titles they are not interested in, one file per user under
/// <c>notinterested/</c> in the plugin data folder, so the web UI surfaces stop showing those titles to that
/// user. A user's file is deleted when the user is (see <see cref="TodoOwner"/>).
/// </summary>
/// <remarks>
/// A list that cannot be read just now reads as empty, so a page still renders, and a change to it, or one
/// that could not be saved, fails with <see cref="UserListUnavailableException"/> rather than write over it or
/// claim to have been made (see <see cref="UserListFiles{TEntry}.Load"/> and <see cref="UserListFiles{TEntry}.Save"/>).
/// This is never the report's dismissal. <see cref="ResolutionStore"/> holds the report's resolutions, which an
/// administrator makes and which hide a gap from everyone; this list is one user's, an administrator's
/// included, and hides a title from that user alone. Nothing writes one on behalf of the other.
/// </remarks>
public sealed class NotInterestedStore
{
    private const int MaxIdLength = 256;
    private const string ListsFolderName = "notinterested";

    private readonly string? _dataFolderOverride;
    private readonly object _lock = new();
    private readonly UserListFiles<NotInterestedEntry> _files;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotInterestedStore"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public NotInterestedStore(ILogger<NotInterestedStore> logger)
        : this(logger, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotInterestedStore"/> class persisting to a specific data
    /// folder (instead of the plugin's). Used by tests.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="dataFolder">The folder to persist the lists in.</param>
    public NotInterestedStore(ILogger<NotInterestedStore> logger, string? dataFolder)
    {
        _dataFolderOverride = dataFolder;
        _files = new UserListFiles<NotInterestedEntry>(logger, () => DataFolder, ListsFolderName, "not-interested list");
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

    /// <summary>
    /// Puts a title on a user's list. A title already there under another gap id is not added twice.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <param name="gap">The gap the title was dismissed from.</param>
    /// <returns><see langword="true"/> when the title was added.</returns>
    public bool Add(Guid userId, GapItem gap)
    {
        RequireUser(userId);
        ArgumentNullException.ThrowIfNull(gap);

        if (string.IsNullOrEmpty(gap.Id) || gap.Id.Length > MaxIdLength)
        {
            return false;
        }

        lock (_lock)
        {
            var map = _files.Load(userId);
            var keys = GapTargetKey.For(gap).ToHashSet(StringComparer.Ordinal);
            if (map.ContainsKey(gap.Id) || map.Values.Any(e => TodoKeys.For(e).Any(keys.Contains)))
            {
                return false;
            }

            map[gap.Id] = Snapshot(gap);
            _files.Save(userId, map);
            return true;
        }
    }

    /// <summary>
    /// Takes an entry off a user's list by its id, as the list itself shows it.
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
            var map = _files.Load(userId);
            if (!map.Remove(id))
            {
                return 0;
            }

            _files.Save(userId, map);
            return 1;
        }
    }

    /// <summary>
    /// Takes every entry about one of the given titles off a user's list, for an undo from a card, which knows
    /// the title but not which gap id it was first dismissed under.
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
            var map = _files.Load(userId);
            var gone = map.Where(pair => TodoKeys.For(pair.Value).Any(keys.Contains)).Select(pair => pair.Key).ToList();
            foreach (var id in gone)
            {
                map.Remove(id);
            }

            if (gone.Count > 0)
            {
                _files.Save(userId, map);
            }

            return gone.Count;
        }
    }

    /// <summary>
    /// Empties a user's list, so the surfaces show them every title again.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <returns>The number of entries removed.</returns>
    public int Clear(Guid userId)
    {
        RequireUser(userId);

        lock (_lock)
        {
            var count = _files.Load(userId).Count;
            if (count > 0)
            {
                _files.Save(userId, new Dictionary<string, NotInterestedEntry>(StringComparer.Ordinal));
            }

            return count;
        }
    }

    /// <summary>
    /// Gets a copy of every entry on a user's list, most recently dismissed first.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <returns>The entries.</returns>
    public IReadOnlyList<NotInterestedEntry> Load(Guid userId)
    {
        RequireUser(userId);

        lock (_lock)
        {
            return _files.Peek(userId).Values.OrderByDescending(e => e.AddedUtc, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>
    /// Gets the identity keys of every title on a user's list, for leaving them out of a surface without reading
    /// the list once per card.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <returns>The keys, and how many titles they came from.</returns>
    public (IReadOnlySet<string> Keys, int Count) Keys(Guid userId)
    {
        RequireUser(userId);

        lock (_lock)
        {
            var map = _files.Peek(userId);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in map.Values)
            {
                keys.UnionWith(TodoKeys.For(entry));
            }

            return (keys, map.Count);
        }
    }

    /// <summary>
    /// Deletes a user's list.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <returns><see langword="true"/> when a list file existed and was removed.</returns>
    public bool Delete(Guid userId)
    {
        RequireUser(userId);

        lock (_lock)
        {
            return _files.Delete(userId);
        }
    }

    /// <summary>
    /// Deletes the list of every user that does not exist, for lists left behind when a user was deleted while
    /// the plugin was not listening. A file whose name is not a user id is left alone.
    /// </summary>
    /// <param name="userExists">Whether a user with this id exists.</param>
    /// <returns>The number of lists deleted.</returns>
    public int Prune(Func<Guid, bool> userExists)
    {
        lock (_lock)
        {
            return _files.Prune(userExists);
        }
    }

    private static void RequireUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A not-interested list must belong to a user.", nameof(userId));
        }
    }

    private static NotInterestedEntry Snapshot(GapItem gap)
        => new()
        {
            Id = gap.Id,
            Name = gap.Name,
            Year = gap.Year,
            TargetKindName = gap.TargetKindName,
            Creator = gap.SourceItemName,
            ImageUrl = gap.ImageUrl,
            ProviderIds = new Dictionary<string, string>(gap.ProviderIds, StringComparer.OrdinalIgnoreCase),
            AddedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };
}
