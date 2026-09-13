using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The per-user "Want to watch" playlist, from the server side: the same playlist the web client creates and
/// edits through Jellyfin's playlist API, found by its configured name among the playlists the user owns.
/// </summary>
public sealed class WatchlistPlaylistService
{
    private readonly IPlaylistManager _playlists;
    private readonly ILogger<WatchlistPlaylistService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistPlaylistService"/> class.
    /// </summary>
    /// <param name="playlists">The playlist manager.</param>
    /// <param name="logger">The logger.</param>
    public WatchlistPlaylistService(IPlaylistManager playlists, ILogger<WatchlistPlaylistService> logger)
    {
        _playlists = playlists;
        _logger = logger;
    }

    /// <summary>
    /// Gets the configured playlist name.
    /// </summary>
    public static string Name => Plugin.Instance?.Configuration.WatchlistName is { Length: > 0 } n ? n : "Want to watch";

    /// <summary>
    /// Finds the user's watchlist playlist.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The playlist, or <see langword="null"/> when the user has none.</returns>
    public Playlist? Find(Guid userId)
        => _playlists.GetPlaylists(userId).FirstOrDefault(p => p.OwnerUserId.Equals(userId) && Matches(p.Name));

    /// <summary>
    /// Determines whether a playlist name is the watchlist's.
    /// </summary>
    /// <param name="name">The playlist name.</param>
    /// <returns><see langword="true"/> when it matches, ignoring case and surrounding whitespace.</returns>
    public static bool Matches(string? name)
        => !string.IsNullOrWhiteSpace(name) && string.Equals(name.Trim(), Name.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a library item to the user's watchlist, creating the playlist on first use.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="itemId">The library item.</param>
    /// <returns>A task.</returns>
    public async Task AddAsync(Guid userId, Guid itemId)
    {
        var playlist = Find(userId);
        if (playlist is null)
        {
            var created = await _playlists.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = Name,
                UserId = userId,
                MediaType = MediaType.Video,
                ItemIdList = [itemId]
            }).ConfigureAwait(false);
            _logger.LogInformation("Watchlist: created playlist '{Name}' ({Id}) for user {User}", Name, created.Id, userId);
            return;
        }

        if (playlist.GetManageableItems().Any(e => e.Item1.ItemId == itemId))
        {
            return;
        }

        await _playlists.AddItemToPlaylistAsync(playlist.Id, [itemId], null, userId).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a library item from the user's watchlist, if it is on it.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="itemId">The library item.</param>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    public async Task<bool> RemoveAsync(Guid userId, Guid itemId)
    {
        var playlist = Find(userId);
        if (playlist is null || !playlist.GetManageableItems().Any(e => e.Item1.ItemId == itemId))
        {
            return false;
        }

        // The playlist manager matches entry ids on the item id.
        await _playlists.RemoveItemFromPlaylistAsync(playlist.Id.ToString("N", CultureInfo.InvariantCulture), [itemId.ToString("N", CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        return true;
    }
}
