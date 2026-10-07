using System;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Builds the home screen's want-to-watch row for one user from their own list, against the same briefly kept
/// index of the owned movies and series the other surfaces use, so a title that has arrived in the library
/// drops off the row without the list being touched. When the row includes owned titles, the ones in the
/// user's want-to-watch playlist lead it, so an arrived title moves along the row rather than off it.
/// </summary>
public sealed class WantedRowService
{
    private static readonly BaseItemKind[] _kinds = [BaseItemKind.Movie, BaseItemKind.Series];

    private readonly TodoStore _todo;
    private readonly OwnershipIndexBuilder _ownershipIndexBuilder;
    private readonly IMemoryCache _cache;
    private readonly IUserManager _users;
    private readonly WatchlistPlaylistService _playlist;

    /// <summary>
    /// Initializes a new instance of the <see cref="WantedRowService"/> class.
    /// </summary>
    /// <param name="todo">The todo store.</param>
    /// <param name="ownershipIndexBuilder">Indexes the owned movies and series.</param>
    /// <param name="cache">The memory cache.</param>
    /// <param name="users">The user manager, for what the user may see of their playlist.</param>
    /// <param name="playlist">Reads the user's want-to-watch playlist.</param>
    public WantedRowService(TodoStore todo, OwnershipIndexBuilder ownershipIndexBuilder, IMemoryCache cache, IUserManager users, WatchlistPlaylistService playlist)
    {
        _todo = todo;
        _ownershipIndexBuilder = ownershipIndexBuilder;
        _cache = cache;
        _users = users;
        _playlist = playlist;
    }

    /// <summary>
    /// Builds the row.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <param name="limit">The most titles to return.</param>
    /// <returns>The row.</returns>
    public WantedRowResult Get(Guid userId, int limit)
    {
        var entries = _todo.Load(userId);
        var owned = _users.GetUserById(userId) is { } user
            ? _playlist.GetOwned(user, Plugin.Instance?.Configuration)
            : [];
        if (entries.Count == 0 && owned.Count == 0)
        {
            return new WantedRowResult();
        }

        if (!_cache.TryGetValue(OwnershipCache.Key, out OwnershipIndex? ownership) || ownership is null)
        {
            ownership = _ownershipIndexBuilder.Build(_kinds);
            _cache.Set(OwnershipCache.Key, ownership, OwnershipCache.Ttl);
        }

        return new WantedRowResult { Titles = WantedRowBuilder.Build(entries, ownership, owned, limit, DateTime.UtcNow) };
    }
}
