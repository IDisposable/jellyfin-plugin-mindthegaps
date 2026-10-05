using System;
using System.Collections.Concurrent;
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
/// <para>
/// Membership is by title. Jellyfin's playlist manager never stores a series: adding one adds every episode
/// (<c>Playlist.GetPlaylistItems</c> expands any folder), so a series is on the playlist when any of its episodes
/// is, and taking the series off takes all of them. Every find-or-create, add and remove for one user runs
/// behind that user's own gate, so two first adds cannot each create a playlist of the same name, and two adds
/// cannot both pass the membership check.
/// </para>
/// </summary>
public sealed class WatchlistPlaylistService
{
    private readonly IPlaylistManager _playlists;
    private readonly LibraryVerifier _verifier;
    private readonly ILogger<WatchlistPlaylistService> _logger;
    private readonly Func<Playlist, IEnumerable<BaseItem>> _entries;
    private readonly Func<BaseItem, BaseItem?> _titleOf;
    private readonly Func<BaseItem, User, bool> _maySee;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistPlaylistService"/> class.
    /// </summary>
    /// <param name="playlists">The host's playlist manager.</param>
    /// <param name="verifier">Resolves a todo entry to the real item that fills it.</param>
    /// <param name="logger">The logger.</param>
    public WatchlistPlaylistService(IPlaylistManager playlists, LibraryVerifier verifier, ILogger<WatchlistPlaylistService> logger)
        : this(playlists, verifier, logger, playlist => playlist.GetLinkedChildren(), DefaultTitleOf, (item, user) => item.IsVisible(user))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistPlaylistService"/> class with explicit lookups.
    /// Test seam: resolving a playlist's entries, an episode's series and a title's visibility all go through
    /// the host's static library manager, which a test has no instance of (10.11's <c>IsVisible</c> walks the
    /// item's parents for inherited tags).
    /// </summary>
    /// <param name="playlists">The host's playlist manager.</param>
    /// <param name="verifier">Resolves a todo entry to the real item that fills it.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="entries">Resolves a playlist's entries to library items, in playlist order.</param>
    /// <param name="titleOf">The movie or series an entry stands for.</param>
    /// <param name="maySee">Whether a user may see a title.</param>
    internal WatchlistPlaylistService(
        IPlaylistManager playlists,
        LibraryVerifier verifier,
        ILogger<WatchlistPlaylistService> logger,
        Func<Playlist, IEnumerable<BaseItem>> entries,
        Func<BaseItem, BaseItem?> titleOf,
        Func<BaseItem, User, bool> maySee)
    {
        _playlists = playlists;
        _verifier = verifier;
        _logger = logger;
        _entries = entries;
        _titleOf = titleOf;
        _maySee = maySee;
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

        try
        {
            var added = await WithGateAsync(userId, () => AddLockedAsync(userId, itemIds, config.WantToWatchPlaylistName)).ConfigureAwait(false);
            _logger.LogDebug(
                "Want to watch: added {Count} title(s) to '{Name}' for user {UserId}",
                added,
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
    /// Puts an owned movie or series on a user's want-to-watch playlist (a movie or series page's bookmark),
    /// creating the playlist the first time. Unlike <see cref="AddArrivedAsync"/> a failure is the caller's to
    /// report, since a user pressed a button and should hear that it did not work.
    /// </summary>
    /// <param name="userId">The playlist's owner.</param>
    /// <param name="itemId">The movie or series.</param>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when added; <see langword="false"/> when it was already there or the playlist is off.</returns>
    /// <exception cref="InvalidOperationException">The playlist could not be created.</exception>
    public async Task<bool> AddItemAsync(Guid userId, Guid itemId, PluginConfiguration? config)
    {
        if (config is not { WantToWatchPlaylistEnabled: true })
        {
            return false;
        }

        var added = await WithGateAsync(userId, () => AddLockedAsync(userId, [itemId], config.WantToWatchPlaylistName)).ConfigureAwait(false);
        return added > 0;
    }

    /// <summary>
    /// Whether a movie or series is on a user's want-to-watch playlist: the movie itself, or any episode of
    /// the series.
    /// </summary>
    /// <param name="userId">The playlist's owner.</param>
    /// <param name="titleId">The movie or series.</param>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when it is there.</returns>
    public bool Contains(Guid userId, Guid titleId, PluginConfiguration? config)
        => config is { WantToWatchPlaylistEnabled: true }
            && Find(userId, config.WantToWatchPlaylistName) is { } playlist
            && EntriesOf(_entries(playlist), titleId).Count > 0;

    /// <summary>
    /// Gets the movies and series on a user's want-to-watch playlist that they may see, the ones added last
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

        return OwnedTitles(_entries(playlist), _titleOf, item => _maySee(item, user));
    }

    /// <summary>
    /// Takes a movie or series off a user's want-to-watch playlist: the movie, or every episode of the series.
    /// </summary>
    /// <param name="userId">The playlist's owner.</param>
    /// <param name="titleId">The movie or series.</param>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when anything was on the playlist and has been removed.</returns>
    public Task<bool> RemoveTitleAsync(Guid userId, Guid titleId, PluginConfiguration? config)
        => RemoveAsync(userId, config, entries => EntriesOf(entries, titleId));

    /// <summary>
    /// Takes one entry off a user's want-to-watch playlist: the movie or episode that was just watched, leaving
    /// a series' other episodes where they are.
    /// </summary>
    /// <param name="userId">The playlist's owner.</param>
    /// <param name="itemId">The movie or episode.</param>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when it was on the playlist and has been removed.</returns>
    public Task<bool> RemoveEntryAsync(Guid userId, Guid itemId, PluginConfiguration? config)
        => RemoveAsync(userId, config, entries => entries.Any(e => e.Id == itemId) ? [itemId] : []);

    /// <summary>
    /// Whether an item is one the want-to-watch playlist keeps: a real movie or series. A minted placeholder
    /// is a virtual Movie or Series with nothing to play, so it never counts.
    /// </summary>
    /// <param name="item">The library item, or <see langword="null"/> when there is none.</param>
    /// <returns><see langword="true"/> for a real movie or series.</returns>
    public static bool IsWantable(BaseItem? item) => item is Movie or Series && !item.IsVirtualItem;

    /// <summary>
    /// The pure half of <see cref="GetOwned"/>: the movies and series a playlist's entries stand for, once each,
    /// newest first, that the user may see. A playlist keeps its entries in the order they were added, so the
    /// newest is last; a series counts from its most recently added episode.
    /// </summary>
    /// <param name="entries">The playlist's resolved entries, in playlist order.</param>
    /// <param name="titleOf">The movie or series an entry stands for.</param>
    /// <param name="maySee">Whether the user may see a title.</param>
    /// <returns>The titles to show.</returns>
    internal static IReadOnlyList<BaseItem> OwnedTitles(IEnumerable<BaseItem?> entries, Func<BaseItem, BaseItem?> titleOf, Func<BaseItem, bool> maySee)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(titleOf);
        ArgumentNullException.ThrowIfNull(maySee);

        // An entry whose item has left the library resolves to nothing and is skipped.
        return entries
            .OfType<BaseItem>()
            .Reverse()
            .Select(titleOf)
            .OfType<BaseItem>()
            .Where(title => IsWantable(title) && maySee(title))
            .DistinctBy(title => title.Id)
            .ToList();
    }

    /// <summary>
    /// The pure membership rule: the ids of the entries that put a title on the playlist, which are the movie
    /// itself, or a series' episodes. These are also the entry ids the playlist manager removes by.
    /// </summary>
    /// <param name="entries">The playlist's resolved entries.</param>
    /// <param name="titleId">The movie or series.</param>
    /// <returns>The matching entries' item ids.</returns>
    internal static IReadOnlyList<Guid> EntriesOf(IEnumerable<BaseItem> entries, Guid titleId)
        => entries
            .Where(e => e.Id == titleId || (e is Episode episode && episode.SeriesId == titleId))
            .Select(e => e.Id)
            .ToList();

    private static BaseItem? DefaultTitleOf(BaseItem entry) => entry is Episode episode ? episode.Series : entry;

    private async Task<T> WithGateAsync<T>(Guid userId, Func<Task<T>> action)
    {
        var gate = _gates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    // Behind the user's gate: finds or creates the playlist and adds whichever titles are not on it yet,
    // checking membership against the playlist as it is now. Returns how many titles were added.
    private async Task<int> AddLockedAsync(Guid userId, IReadOnlyList<Guid> titleIds, string name)
    {
        var existing = Find(userId, name);
        var entries = existing is null ? [] : _entries(existing).ToList();
        var toAdd = titleIds.Distinct().Where(id => EntriesOf(entries, id).Count == 0).ToList();
        if (toAdd.Count == 0)
        {
            return 0;
        }

        var playlistId = existing?.Id
            ?? await CreatePlaylistAsync(userId, name).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not create the want-to-watch playlist.");

#if NET10_0_OR_GREATER
        await _playlists.AddItemToPlaylistAsync(playlistId, toAdd, null, userId).ConfigureAwait(false);
#else
        await _playlists.AddItemToPlaylistAsync(playlistId, toAdd, userId).ConfigureAwait(false);
#endif
        return toAdd.Count;
    }

    private Task<bool> RemoveAsync(Guid userId, PluginConfiguration? config, Func<IReadOnlyList<BaseItem>, IReadOnlyList<Guid>> select)
    {
        if (config is not { WantToWatchPlaylistEnabled: true })
        {
            return Task.FromResult(false);
        }

        var name = config.WantToWatchPlaylistName;
        return WithGateAsync(userId, async () =>
        {
            if (Find(userId, name) is not { } playlist)
            {
                return false;
            }

            var ids = select(_entries(playlist).ToList());
            if (ids.Count == 0)
            {
                return false;
            }

            // The playlist manager identifies an entry by its item's id, in the "N" form.
            await _playlists.RemoveItemFromPlaylistAsync(
                playlist.Id.ToString("N", CultureInfo.InvariantCulture),
                ids.Select(id => id.ToString("N", CultureInfo.InvariantCulture))).ConfigureAwait(false);
            return true;
        });
    }

    // Only a playlist the user owns: GetPlaylists also returns ones shared with them, and another user's
    // playlist of the same name is not theirs to add to or take from.
    private Playlist? Find(Guid userId, string name)
        => _playlists.GetPlaylists(userId)
            .FirstOrDefault(p => p.OwnerUserId.Equals(userId) && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

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
