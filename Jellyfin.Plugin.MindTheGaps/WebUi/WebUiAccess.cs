using System;
using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Decides what the signed-in user may be shown by the web UI surfaces, kept to lookups the server already
/// has in memory so it costs a request nothing measurable. A user with a parental rating limit is not shown
/// the surfaces at all: what they list is TMDB's, not the library's, and its certifications would take a
/// TMDB request per title to check. A page is shown only to a user who can see the item it is about.
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
    /// a user's (an API key) is not restricted.
    /// </summary>
    /// <param name="principal">The request's principal.</param>
    /// <param name="itemId">The item a page is about, or <see langword="null"/> for the home screen.</param>
    /// <returns><see langword="false"/> for a restricted user, or a user who cannot see the item.</returns>
    public bool MaySee(ClaimsPrincipal? principal, Guid? itemId = null)
    {
        if (!TodoOwner.TryGetUserId(principal, out var userId) || _users.GetUserById(userId) is not { } user)
        {
            return true;
        }

        if (IsRestricted(user))
        {
            return false;
        }

        return itemId is not { } id || _library.GetItemById(id) is not BaseItem item || item.IsVisible(user);
    }

    /// <summary>
    /// Gets the caller's id when they may keep a want-to-watch list: a signed-in user without a parental rating
    /// limit. An administrator's first call also takes over the old server-wide todo list.
    /// </summary>
    /// <param name="principal">The request's principal.</param>
    /// <param name="isAdministrator">Whether the caller is an administrator.</param>
    /// <returns>The user's id, or <see langword="null"/>.</returns>
    public Guid? WantingUser(ClaimsPrincipal? principal, bool isAdministrator)
    {
        if (_owner.Resolve(principal, isAdministrator) is not { } userId || _users.GetUserById(userId) is not { } user || IsRestricted(user))
        {
            return null;
        }

        return userId;
    }
}
