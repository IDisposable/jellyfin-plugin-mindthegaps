using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Takes a title off the viewer's watchlist once they have watched it, on the server, so it works whichever
/// client they watched on. Listens for Jellyfin saving a user's played state: a movie comes off when it is
/// played; a series comes off when every episode is, so a show in progress stays.
/// </summary>
public sealed class WatchedAutoRemover : IHostedService
{
    private readonly IUserDataManager _userData;
    private readonly IUserManager _users;
    private readonly WatchlistPlaylistService _watchlist;
    private readonly ILogger<WatchedAutoRemover> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchedAutoRemover"/> class.
    /// </summary>
    /// <param name="userData">The user data manager, whose saves are listened for.</param>
    /// <param name="users">The user manager.</param>
    /// <param name="watchlist">The watchlist playlist.</param>
    /// <param name="logger">The logger.</param>
    public WatchedAutoRemover(IUserDataManager userData, IUserManager users, WatchlistPlaylistService watchlist, ILogger<WatchedAutoRemover> logger)
    {
        _userData = userData;
        _users = users;
        _watchlist = watchlist;
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
        if (config is null || !config.WantToWatchEnabled || !config.WatchlistAutoRemoveWatched
            || e.Item is null || !IsWatchedSave(e.SaveReason, e.UserData?.Played == true))
        {
            return;
        }

        // Off the event thread; a playlist update is I/O and must not hold Jellyfin's user-data save.
        _ = Task.Run(() => RemoveWatchedAsync(e.UserId, e.Item));
    }

    private async Task RemoveWatchedAsync(Guid userId, BaseItem item)
    {
        try
        {
            var user = _users.GetUserById(userId);
            if (user is null)
            {
                return;
            }

            foreach (var candidate in Candidates(item, user))
            {
                if (await _watchlist.RemoveAsync(userId, candidate.Id).ConfigureAwait(false))
                {
                    _logger.LogInformation("Watchlist: removed '{Name}' for user {User}, watched", candidate.Name, user.Username);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watchlist: could not remove a watched title for user {User}", userId);
        }
    }

    // The item itself, and for an episode or season its series once the whole series is played.
    private static IEnumerable<BaseItem> Candidates(BaseItem item, User user)
    {
        yield return item;

        var series = item switch
        {
            Episode e => e.Series,
            Season s => s.Series,
            _ => null
        };
        if (series is not null && series.IsPlayed(user, null!))
        {
            yield return series;
        }
    }
}
