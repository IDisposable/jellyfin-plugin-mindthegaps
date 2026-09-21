using System;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Builds the home screen's want-to-watch row for one user from their own list, against the same briefly kept
/// index of the owned movies and series the other surfaces use, so a title that has arrived in the library
/// drops off the row without the list being touched.
/// </summary>
public sealed class WantedRowService
{
    private static readonly BaseItemKind[] _kinds = [BaseItemKind.Movie, BaseItemKind.Series];

    private readonly TodoStore _todo;
    private readonly OwnershipIndexBuilder _ownershipIndexBuilder;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="WantedRowService"/> class.
    /// </summary>
    /// <param name="todo">The todo store.</param>
    /// <param name="ownershipIndexBuilder">Indexes the owned movies and series.</param>
    /// <param name="cache">The memory cache.</param>
    public WantedRowService(TodoStore todo, OwnershipIndexBuilder ownershipIndexBuilder, IMemoryCache cache)
    {
        _todo = todo;
        _ownershipIndexBuilder = ownershipIndexBuilder;
        _cache = cache;
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
        if (entries.Count == 0)
        {
            return new WantedRowResult();
        }

        if (!_cache.TryGetValue(OwnershipCache.Key, out OwnershipIndex? ownership) || ownership is null)
        {
            ownership = _ownershipIndexBuilder.Build(_kinds);
            _cache.Set(OwnershipCache.Key, ownership, OwnershipCache.Ttl);
        }

        return new WantedRowResult { Titles = WantedRowBuilder.Build(entries, ownership, limit, DateTime.UtcNow) };
    }
}
