using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Takes a title off the viewer's want-to-watch playlist once they have watched it
/// (<see cref="PluginConfiguration.WantToWatchRemoveWatched"/>). Listens for Jellyfin saving a user's played
/// state, so it works whichever client they watched on: a movie or episode comes off when it is played, and a
/// series' remaining episodes once every one of them is.
/// </summary>
internal sealed class WatchedAutoRemover : IHostedService
{
    // Marking a whole series played saves every episode in turn, each its own event. Waiting this long before
    // asking whether the series is finished lets one check answer for the whole burst.
    private static readonly TimeSpan SeriesSettle = TimeSpan.FromSeconds(5);

    private readonly IUserDataManager _userData;
    private readonly IUserManager _users;
    private readonly WatchlistPlaylistService _playlist;
    private readonly PluginLifetime _lifetime;
    private readonly ILogger<WatchedAutoRemover> _logger;
    private readonly ConcurrentDictionary<(Guid User, Guid Series), byte> _pendingSeries = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchedAutoRemover"/> class.
    /// </summary>
    /// <param name="userData">The user data manager, whose saves are listened for.</param>
    /// <param name="users">The user manager.</param>
    /// <param name="playlist">The want-to-watch playlist.</param>
    /// <param name="lifetime">Stops a pending removal on shutdown.</param>
    /// <param name="logger">The logger.</param>
    public WatchedAutoRemover(IUserDataManager userData, IUserManager users, WatchlistPlaylistService playlist, PluginLifetime lifetime, ILogger<WatchedAutoRemover> logger)
    {
        _userData = userData;
        _users = users;
        _playlist = playlist;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userData.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userData.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether removing watched titles is switched on: it needs want to watch and its playlist, which is where
    /// a watched title would be removed from.
    /// </summary>
    /// <param name="config">The configuration, or <see langword="null"/> before the plugin is initialized.</param>
    /// <returns><see langword="true"/> when on.</returns>
    public static bool IsOn(PluginConfiguration? config)
        => config is { WantToWatchEnabled: true, WantToWatchPlaylistEnabled: true, WantToWatchRemoveWatched: true };

    /// <summary>
    /// Whether a save is one that can mean "watched": playback finishing, or the played flag being set by
    /// hand, and the resulting state is played. Progress ticks and playback starts never are.
    /// </summary>
    /// <param name="reason">The save reason.</param>
    /// <param name="played">The saved played flag.</param>
    /// <returns><see langword="true"/> when the item may now count as watched.</returns>
    public static bool IsWatchedSave(UserDataSaveReason reason, bool played)
        => played && reason is UserDataSaveReason.PlaybackFinished or UserDataSaveReason.TogglePlayed;

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (!IsOn(config) || e.Item is null || !IsWatchedSave(e.SaveReason, e.UserData?.Played == true))
        {
            return;
        }

        // Off the event thread: a playlist update writes the playlist, and must not hold up Jellyfin's save.
        var stopping = _lifetime.Stopping;
        _ = Task.Run(() => RemoveWatchedAsync(e.UserId, e.Item, config, stopping), stopping);
    }

    private async Task RemoveWatchedAsync(Guid userId, BaseItem item, PluginConfiguration? config, CancellationToken stopping)
    {
        try
        {
            if (_users.GetUserById(userId) is not { } user)
            {
                return;
            }

            // The movie or episode just watched comes off by itself; a series' other episodes stay.
            if (await _playlist.RemoveEntryAsync(userId, item.Id, config).ConfigureAwait(false))
            {
                _logger.LogInformation("Want to watch: removed '{Name}' from {User}'s playlist, watched", item.Name, user.Username);
            }

            // One check per user and series at a time; a save during the wait is answered by that check.
            if (SeriesOf(item) is { } series && _pendingSeries.TryAdd((userId, series.Id), 0))
            {
                await RemoveIfFinishedAsync(user, series, config, stopping).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Shutting down; the title stays on the playlist.
        }
        catch (Exception ex)
        {
            // A background removal has no caller to report to; the title just stays on the playlist.
            _logger.LogWarning(ex, "Want to watch: could not remove a watched title for user {UserId}", userId);
        }
    }

    // Once every episode is played the series is done, along with anything of it still on the playlist.
    private async Task RemoveIfFinishedAsync(User user, Series series, PluginConfiguration? config, CancellationToken stopping)
    {
        try
        {
            await Task.Delay(SeriesSettle, stopping).ConfigureAwait(false);
        }
        finally
        {
            // Released before the check, so a save that lands while it runs schedules a check of its own.
            _pendingSeries.TryRemove((user.Id, series.Id), out _);
        }

        // Whether the series is played walks all its episodes, so ask only when the playlist holds any of it.
        if (_playlist.Contains(user.Id, series.Id, config)
            && series.IsPlayed(user, null)
            && await _playlist.RemoveTitleAsync(user.Id, series.Id, config).ConfigureAwait(false))
        {
            _logger.LogInformation("Want to watch: removed '{Name}' from {User}'s playlist, every episode watched", series.Name, user.Username);
        }
    }

    private static Series? SeriesOf(BaseItem item) => item switch
    {
        Episode episode => episode.Series,
        Season season => season.Series,
        Series whole => whole,
        _ => null
    };
}
