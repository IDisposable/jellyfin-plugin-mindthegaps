using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
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

        var playlistId = await FindOrCreatePlaylistAsync(userId, config.WantToWatchPlaylistName).ConfigureAwait(false);
        if (playlistId is null)
        {
            return;
        }

        try
        {
#if NET10_0_OR_GREATER
            await _playlists.AddItemToPlaylistAsync(playlistId.Value, itemIds, null, userId).ConfigureAwait(false);
#else
            await _playlists.AddItemToPlaylistAsync(playlistId.Value, itemIds, userId).ConfigureAwait(false);
#endif
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

    private async Task<Guid?> FindOrCreatePlaylistAsync(Guid userId, string name)
    {
        var existing = _playlists.GetPlaylists(userId)
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing.Id;
        }

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
