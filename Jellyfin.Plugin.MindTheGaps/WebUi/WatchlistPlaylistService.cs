using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Moves a want-to-watch entry into a real, per-user Jellyfin playlist once the library actually holds it
/// (a real, non-virtual item; a minted placeholder never satisfies <see cref="LibraryVerifier"/>, so minting
/// can never trigger this regardless of whether minting is on). Opt-in
/// (<see cref="Configuration.PluginConfiguration.WantToWatchPlaylistEnabled"/>), called from the same place a
/// todo entry already flips to done (<c>Todo/Verify</c>/<c>Todo/VerifyAll</c>), never at add time: at add
/// time there is no real item yet to put in a playlist.
/// </summary>
public sealed class WatchlistPlaylistService
{
    private readonly IPlaylistManager _playlists;
    private readonly LibraryVerifier _verifier;
    private readonly ILogger<WatchlistPlaylistService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistPlaylistService"/> class.
    /// </summary>
    /// <param name="playlists">The host's playlist manager.</param>
    /// <param name="verifier">Resolves a todo entry to the real item that fills it.</param>
    /// <param name="logger">The logger.</param>
    public WatchlistPlaylistService(IPlaylistManager playlists, LibraryVerifier verifier, ILogger<WatchlistPlaylistService> logger)
    {
        _playlists = playlists;
        _verifier = verifier;
        _logger = logger;
    }

    /// <summary>
    /// Adds whichever of these entries just arrived (movies and series only; a playlist is for watching) to
    /// the user's own want-to-watch playlist, finding or creating it by name. A no-op while the feature is
    /// off, or when none of the entries resolve to a real item.
    /// </summary>
    /// <param name="userId">The list's owner.</param>
    /// <param name="arrivedEntries">The entries that were just found owned (not every entry on the list).</param>
    /// <param name="config">The plugin configuration, read by the caller rather than here so this stays
    /// callable without a live <see cref="Plugin.Instance"/> (a null config is treated the same as the
    /// feature being off, matching a plugin that has not finished starting up).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the playlist has been updated, if it needed to be.</returns>
    public async Task AddArrivedAsync(Guid userId, IReadOnlyList<TodoEntry> arrivedEntries, PluginConfiguration? config, CancellationToken cancellationToken)
    {
        if (config is not { WantToWatchPlaylistEnabled: true } || arrivedEntries.Count == 0)
        {
            return;
        }

        var itemIds = new List<Guid>();
        foreach (var entry in arrivedEntries)
        {
            if (entry.TargetKindName is not (nameof(BaseItemKind.Movie) or nameof(BaseItemKind.Series))
                || !Enum.TryParse<BaseItemKind>(entry.TargetKindName, out var kind))
            {
                continue;
            }

            if (_verifier.FindOwnedItemId(kind, entry.ProviderIds, entry.Creator, entry.Name) is { } itemId)
            {
                itemIds.Add(itemId);
            }
        }

        if (itemIds.Count == 0)
        {
            _logger.LogDebug(
                "Want to watch: {Count} entries for user {UserId} verified owned, but none resolved to a real library item",
                arrivedEntries.Count,
                userId);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // A title the user already put on the playlist themselves (the page bookmark) is not added twice.
        var existing = Find(userId, config.WantToWatchPlaylistName);
        if (existing is not null)
        {
            itemIds.RemoveAll(id => Holds(existing, id));
            if (itemIds.Count == 0)
            {
                return;
            }
        }

        var playlistId = existing?.Id ?? await CreatePlaylistAsync(userId, config.WantToWatchPlaylistName).ConfigureAwait(false);
        if (playlistId is null)
        {
            return;
        }

        try
        {
            await AddToPlaylistAsync(playlistId.Value, itemIds, userId).ConfigureAwait(false);
            _logger.LogDebug(
                "Want to watch: added {Count} item(s) to '{Name}' for user {UserId}",
                itemIds.Count,
                config.WantToWatchPlaylistName,
                userId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let a playlist problem fault the caller: Todo/Verify(All) has already marked the entry
            // done and that has to stand regardless of whether the playlist add went through.
            _logger.LogWarning(
                ex,
                "Could not add {Count} item(s) to the want-to-watch playlist '{Name}' for user {UserId}",
                itemIds.Count,
                config.WantToWatchPlaylistName,
                userId);
        }
    }

    /// <summary>
    /// Puts an owned item on a user's want-to-watch playlist (a movie or series page's bookmark), creating the
    /// playlist the first time. Unlike <see cref="AddArrivedAsync"/> a failure is the caller's to report, since
    /// a user pressed a button and should hear that it did not work.
    /// </summary>
    /// <param name="userId">The playlist's owner.</param>
    /// <param name="itemId">The library item.</param>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when added; <see langword="false"/> when it was already there or the playlist is off.</returns>
    /// <exception cref="InvalidOperationException">The playlist could not be created.</exception>
    public async Task<bool> AddItemAsync(Guid userId, Guid itemId, PluginConfiguration? config)
    {
        if (config is not { WantToWatchPlaylistEnabled: true })
        {
            return false;
        }

        var existing = Find(userId, config.WantToWatchPlaylistName);
        if (existing is not null && Holds(existing, itemId))
        {
            return false;
        }

        var playlistId = existing?.Id
            ?? await CreatePlaylistAsync(userId, config.WantToWatchPlaylistName).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not create the want-to-watch playlist.");
        await AddToPlaylistAsync(playlistId, [itemId], userId).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Whether an item is on a user's want-to-watch playlist.
    /// </summary>
    /// <param name="userId">The playlist's owner.</param>
    /// <param name="itemId">The library item.</param>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when it is there.</returns>
    public bool Contains(Guid userId, Guid itemId, PluginConfiguration? config)
        => config is { WantToWatchPlaylistEnabled: true }
            && Find(userId, config.WantToWatchPlaylistName) is { } playlist
            && Holds(playlist, itemId);

    /// <summary>
    /// Gets the movies and series in a user's want-to-watch playlist that they may see, the ones added last
    /// first, for the home row. Empty unless the playlist and the row's owned titles are both switched on.
    /// </summary>
    /// <param name="user">The playlist's owner.</param>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns>The owned titles to show.</returns>
    public IReadOnlyList<BaseItem> GetOwned(User user, PluginConfiguration? config)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (config is not { WantToWatchPlaylistEnabled: true, WantToWatchRowIncludesOwned: true }
            || Find(user.Id, config.WantToWatchPlaylistName) is not { } playlist)
        {
            return [];
        }

        return OwnedTitles(playlist.GetLinkedChildren(), item => item.IsVisible(user));
    }

    /// <summary>
    /// The pure half of <see cref="GetOwned"/>: the movies and series among a playlist's items that the user may
    /// see, newest first. A playlist keeps its entries in the order they were added, so the newest is last.
    /// </summary>
    /// <param name="linked">The playlist's resolved items, in playlist order.</param>
    /// <param name="maySee">Whether the user may see an item.</param>
    /// <returns>The titles to show.</returns>
    internal static IReadOnlyList<BaseItem> OwnedTitles(IEnumerable<BaseItem?> linked, Func<BaseItem, bool> maySee)
    {
        ArgumentNullException.ThrowIfNull(linked);
        ArgumentNullException.ThrowIfNull(maySee);

        // An entry whose item has left the library resolves to nothing and is skipped.
        return linked
            .OfType<BaseItem>()
            .Where(item => IsWantable(item) && maySee(item))
            .Reverse()
            .ToList();
    }

    /// <summary>
    /// Takes an item off a user's want-to-watch playlist, if it is there.
    /// </summary>
    /// <param name="userId">The playlist's owner.</param>
    /// <param name="itemId">The library item.</param>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when the item was on the playlist and has been removed.</returns>
    public async Task<bool> RemoveAsync(Guid userId, Guid itemId, PluginConfiguration? config)
    {
        if (config is not { WantToWatchPlaylistEnabled: true }
            || Find(userId, config.WantToWatchPlaylistName) is not { } playlist
            || !Holds(playlist, itemId))
        {
            return false;
        }

        // The playlist manager identifies an entry by its item's id, in the "N" form.
        await _playlists.RemoveItemFromPlaylistAsync(
            playlist.Id.ToString("N", CultureInfo.InvariantCulture),
            [itemId.ToString("N", CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        return true;
    }

    // Only a playlist the user owns: GetPlaylists also returns ones shared with them, and another user's
    // playlist of the same name is not theirs to add to or take from.
    private Playlist? Find(Guid userId, string name)
        => _playlists.GetPlaylists(userId)
            .FirstOrDefault(p => p.OwnerUserId.Equals(userId) && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether an item is one the want-to-watch playlist keeps: a movie or a series.
    /// </summary>
    /// <param name="item">The library item, or <see langword="null"/> when there is none.</param>
    /// <returns><see langword="true"/> for a movie or series.</returns>
    public static bool IsWantable(BaseItem? item) => item is Movie or Series;

    private static bool Holds(Playlist playlist, Guid itemId)
        => playlist.LinkedChildren.Any(child => child.ItemId == itemId);

    private Task AddToPlaylistAsync(Guid playlistId, IReadOnlyCollection<Guid> itemIds, Guid userId)
    {
#if NET10_0_OR_GREATER
        return _playlists.AddItemToPlaylistAsync(playlistId, itemIds, null, userId);
#else
        return _playlists.AddItemToPlaylistAsync(playlistId, itemIds, userId);
#endif
    }

    private async Task<Guid?> CreatePlaylistAsync(Guid userId, string name)
    {
        try
        {
            var result = await _playlists.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = name,
                UserId = userId,
                MediaType = MediaType.Video,
                ItemIdList = []
            }).ConfigureAwait(false);

            return Guid.TryParse(result?.Id, out var id) ? id : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not create the want-to-watch playlist '{Name}' for user {UserId}", name, userId);
            return null;
        }
    }
}
