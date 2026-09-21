using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Identifies whose todo list a request is for, and deletes a list when its user is deleted.
/// </summary>
/// <remarks>
/// The owner is the signed-in user, read from the id the server puts in the request's claims. A request that
/// is not a user's, such as one made with an API key, has no owner and so no list. The first administrator to
/// use the todo list after an upgrade also takes over the old server-wide list
/// (<see cref="TodoStore.AdoptLegacy"/>). A list is deleted when the server reports its user deleted; lists
/// of users deleted while the plugin was not listening are swept once per run, the first time a list is used.
/// </remarks>
public sealed class TodoOwner : IEventConsumer<UserDeletedEventArgs>
{
    /// <summary>
    /// The claim the server puts a signed-in user's id in.
    /// </summary>
    public const string UserIdClaim = "Jellyfin-UserId";

    private readonly TodoStore _store;
    private readonly IUserManager _users;
    private readonly ILogger<TodoOwner> _logger;
    private int _swept;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoOwner"/> class.
    /// </summary>
    /// <param name="store">The todo store.</param>
    /// <param name="users">The user manager.</param>
    /// <param name="logger">The logger.</param>
    public TodoOwner(TodoStore store, IUserManager users, ILogger<TodoOwner> logger)
    {
        _store = store;
        _users = users;
        _logger = logger;
    }

    /// <summary>
    /// Reads the signed-in user's id off a request's principal.
    /// </summary>
    /// <param name="principal">The request's principal.</param>
    /// <param name="userId">The user's id.</param>
    /// <returns><see langword="false"/> when the request is not a user's (an API key carries none).</returns>
    public static bool TryGetUserId(ClaimsPrincipal? principal, out Guid userId)
        => Guid.TryParse(principal?.FindFirst(UserIdClaim)?.Value, out userId) && userId != Guid.Empty;

    /// <summary>
    /// Resolves the caller's list. An administrator's first call after an upgrade also takes over the old
    /// server-wide list.
    /// </summary>
    /// <param name="principal">The request's principal.</param>
    /// <param name="isAdministrator">Whether the caller is an administrator.</param>
    /// <returns>The owner's id, or <see langword="null"/> when the request is not a user's.</returns>
    public Guid? Resolve(ClaimsPrincipal? principal, bool isAdministrator)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return null;
        }

        if (isAdministrator)
        {
            _store.AdoptLegacy(userId);
        }

        SweepOnce();
        return userId;
    }

    /// <summary>
    /// Whether a user with this id exists on the server.
    /// </summary>
    /// <param name="userId">The user's id.</param>
    /// <returns><see langword="true"/> for an existing user.</returns>
    public bool Exists(Guid userId)
        => userId != Guid.Empty && _users.GetUserById(userId) is not null;

    /// <summary>
    /// Gets a user's name for showing beside their list.
    /// </summary>
    /// <param name="userId">The user's id.</param>
    /// <returns>The user's name, or a placeholder for one that does not exist.</returns>
    public string NameOf(Guid userId)
        => (userId == Guid.Empty ? null : _users.GetUserById(userId)?.Username) ?? "Unknown user";

    /// <inheritdoc />
    public Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        if (_store.Delete(eventArgs.Argument.Id))
        {
            _logger.LogInformation("Deleted the todo list of the removed user {User}", eventArgs.Argument.Id);
        }

        return Task.CompletedTask;
    }

    // Deletes the lists of users that do not exist, the first time a list is used in this run. Only an
    // authenticated request gets here, so the server's users are loaded.
    private void SweepOnce()
    {
        if (Interlocked.Exchange(ref _swept, 1) != 0)
        {
            return;
        }

        // Cleanup must never fail the request that happened to trigger it.
        try
        {
            var deleted = _store.Prune(id => _users.GetUserById(id) is not null);
            if (deleted > 0)
            {
                _logger.LogInformation("Deleted {Count} todo lists whose users do not exist", deleted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sweep the todo lists of deleted users");
        }
    }
}
