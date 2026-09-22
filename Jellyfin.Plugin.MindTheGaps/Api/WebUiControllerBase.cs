using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// Shared plumbing for the web UI surface controllers (<see cref="PersonWebUiController"/>,
/// <see cref="ItemWebUiController"/>, <see cref="HomeWebUiController"/>): the want-to-watch add/remove
/// every surface offers on its cards. Abstract and carries no route of its own, so it is never itself
/// discovered as a controller. Deliberately has no acquisition handoff: an administrator monitors what
/// everyone wants through the report's own Maintenance section (the fulfillment queue) instead, so this
/// surface never talks to Radarr or Sonarr.
/// </summary>
public abstract class WebUiControllerBase : ControllerBase
{
    private readonly TodoStore _todo;
    private readonly WebUiAccess _access;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebUiControllerBase"/> class.
    /// </summary>
    /// <param name="todo">The per-user todo-list store, for the want-to-watch add/remove.</param>
    /// <param name="access">Decides what the signed-in user may be shown.</param>
    protected WebUiControllerBase(TodoStore todo, WebUiAccess access)
    {
        _todo = todo;
        _access = access;
    }

    /// <summary>
    /// Gets a value indicating whether the caller is an administrator.
    /// </summary>
    protected bool IsAdministrator => User.IsInRole("Administrator");

    /// <summary>
    /// Gets a value indicating whether the want-to-watch feature is enabled at all.
    /// </summary>
    protected static bool WantToWatchEnabled => WebUiGate.WantToWatch(Plugin.Instance?.Configuration);

    /// <summary>
    /// Gets the service that decides what the signed-in user may be shown.
    /// </summary>
    protected WebUiAccess Access => _access;

    /// <summary>
    /// The caller's own list when they may keep one (want to watch is on, and they are a signed-in user
    /// without a parental rating limit), with the identity keys of what is on it so a card can say whether
    /// its title is.
    /// </summary>
    /// <returns>The caller's user id (null when they may not keep a list) and their wanted keys.</returns>
    protected (Guid? UserId, IReadOnlySet<string> Wanted) Wanting()
    {
        if (!WantToWatchEnabled || _access.WantingUser(User, IsAdministrator) is not { } userId)
        {
            return (null, new HashSet<string>(StringComparer.Ordinal));
        }

        return (userId, _todo.WantedKeys(userId));
    }

    /// <summary>
    /// Puts a card's title on the caller's list, or takes it off. The gap is rehydrated from the same lookup
    /// the page listed it from rather than trusted from the client, so only a title the page could show can
    /// be added. A removal goes by the title, so an entry that reached the list some other way (the report,
    /// another page) comes off too, and by the id when the page does not list the gap. A plain count (0 or 1
    /// added, or however many removed) matches <c>Api/TodoController</c>'s own contract.
    /// </summary>
    /// <param name="surfaceEnabled">Whether the calling surface's toggle is on.</param>
    /// <param name="itemId">The owning item, for the parental-rating check; null for the home surface.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="findGap">Rehydrates the gap server-side from the same lookup the page listed it from.</param>
    /// <param name="add">True to add the gap; false to remove it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries added or removed, or 404 while the surface is off.</returns>
    protected async Task<ActionResult<int>> WantOwnedGapAsync(
        bool surfaceEnabled,
        Guid? itemId,
        string? gapId,
        Func<CancellationToken, Task<GapItem?>> findGap,
        bool add,
        CancellationToken cancellationToken)
    {
        if (!surfaceEnabled || !WantToWatchEnabled || !_access.MaySee(User, itemId))
        {
            return NotFound();
        }

        if (_access.WantingUser(User, IsAdministrator) is not { } userId)
        {
            return Forbid();
        }

        var gap = await findGap(cancellationToken).ConfigureAwait(false);
        if (add)
        {
            return gap is null ? 0 : _todo.Add(userId, [gap]);
        }

        var removed = gap is null ? 0 : _todo.RemoveMatching(userId, GapTargetKey.For(gap).ToList());
        return removed > 0 ? removed : _todo.Remove(userId, gapId ?? string.Empty);
    }
}
