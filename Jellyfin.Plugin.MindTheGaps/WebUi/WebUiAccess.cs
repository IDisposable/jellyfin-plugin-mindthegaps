using System;
using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Decides what the signed-in user may be shown by the web UI surfaces, kept to lookups the server already
/// has in memory so it costs a request nothing measurable. A page is shown only to a user who can see the item
/// it is about. A user with a parental rating limit is shown the surfaces too, and <see cref="RestrictedUser"/>
/// is what tells a surface to narrow its titles to that limit (<see cref="CertificationFilter"/>).
/// </summary>
public sealed class WebUiAccess
{
    private readonly IUserManager _users;
    private readonly ILibraryManager _library;
    private readonly TodoOwner _owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebUiAccess"/> class.
    /// </summary>
    /// <param name="users">The user manager.</param>
    /// <param name="library">The library manager.</param>
    /// <param name="owner">Resolves whose todo list a request is for.</param>
    public WebUiAccess(IUserManager users, ILibraryManager library, TodoOwner owner)
    {
        _users = users;
        _library = library;
        _owner = owner;
    }

    /// <summary>
    /// Whether a user has a parental rating limit.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns><see langword="true"/> when a maximum rating is set.</returns>
    public static bool IsRestricted(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.MaxParentalRatingScore is not null;
    }

    /// <summary>
    /// Whether the caller may be shown a surface, and for a page, the item it is about. A request that is not
    /// a user's (an API key) sees every surface.
    /// </summary>
    /// <param name="principal">The request's principal.</param>
    /// <param name="itemId">The item a page is about, or <see langword="null"/> for the home screen.</param>
    /// <returns><see langword="false"/> for a user who cannot see the item.</returns>
    public bool MaySee(ClaimsPrincipal? principal, Guid? itemId = null)
    {
        if (!TodoOwner.TryGetUserId(principal, out var userId) || _users.GetUserById(userId) is not { } user)
        {
            return true;
        }

        return itemId is not { } id || _library.GetItemById(id) is not BaseItem item || item.IsVisible(user);
    }

    /// <summary>
    /// Gets the caller when they have a parental rating limit, so a surface narrows its titles to it.
    /// </summary>
    /// <param name="principal">The request's principal.</param>
    /// <returns>The user, or <see langword="null"/> for a caller with no limit or a request that is not a
    /// user's.</returns>
    public User? RestrictedUser(ClaimsPrincipal? principal)
        => TodoOwner.TryGetUserId(principal, out var userId) && _users.GetUserById(userId) is { } user && IsRestricted(user)
            ? user
            : null;

    /// <summary>
    /// Gets the caller's id when they may keep a list of their own (want to watch, not interested): a signed-in
    /// user. An administrator's first call also takes over the old server-wide todo list.
    /// </summary>
    /// <param name="principal">The request's principal.</param>
    /// <param name="isAdministrator">Whether the caller is an administrator.</param>
    /// <returns>The user's id, or <see langword="null"/>.</returns>
    public Guid? WantingUser(ClaimsPrincipal? principal, bool isAdministrator)
        => _owner.Resolve(principal, isAdministrator) is { } userId && _users.GetUserById(userId) is not null ? userId : null;
}
