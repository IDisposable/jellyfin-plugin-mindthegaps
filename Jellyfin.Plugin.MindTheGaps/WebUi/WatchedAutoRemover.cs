using System;
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
public sealed class WatchedAutoRemover : IHostedService
{
    private readonly IUserDataManager _userData;
    private readonly IUserManager _users;
    private readonly WatchlistPlaylistService _playlist;
    private readonly ILogger<WatchedAutoRemover> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchedAutoRemover"/> class.
    /// </summary>
    /// <param name="userData">The user data manager, whose saves are listened for.</param>
    /// <param name="users">The user manager.</param>
    /// <param name="playlist">The want-to-watch playlist.</param>
    /// <param name="logger">The logger.</param>
    public WatchedAutoRemover(IUserDataManager userData, IUserManager users, WatchlistPlaylistService playlist, ILogger<WatchedAutoRemover> logger)
    {
        _userData = userData;
        _users = users;
        _playlist = playlist;
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
        _ = Task.Run(() => RemoveWatchedAsync(e.UserId, e.Item, config));
    }

    private async Task RemoveWatchedAsync(Guid userId, BaseItem item, PluginConfiguration? config)
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

            // Once every episode is played the series is done, along with anything of it still on the playlist.
            if (FinishedSeries(item, user) is { } series
                && await _playlist.RemoveTitleAsync(userId, series.Id, config).ConfigureAwait(false))
            {
                _logger.LogInformation("Want to watch: removed '{Name}' from {User}'s playlist, every episode watched", series.Name, user.Username);
            }
        }
        catch (Exception ex)
        {
            // A background removal has no caller to report to; the title just stays on the playlist.
            _logger.LogWarning(ex, "Want to watch: could not remove a watched title for user {UserId}", userId);
        }
    }

    // For an episode or a season, its series once the whole series is played; otherwise null.
    private static Series? FinishedSeries(BaseItem item, User user)
    {
        var series = item switch
        {
            Episode episode => episode.Series,
            Season season => season.Series,
            Series whole => whole,
            _ => null
        };
        return series is not null && series.IsPlayed(user, null) ? series : null;
    }
}
