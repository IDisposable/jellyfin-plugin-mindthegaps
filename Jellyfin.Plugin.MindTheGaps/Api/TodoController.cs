using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// Endpoints for the todo lists (gaps a user marked to acquire): each user's own and, for the administrators
/// who use this page, everyone's, including verifying an entry against the library. Shares the
/// <c>MindTheGaps</c> route.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("MindTheGaps")]
[Produces("application/json")]
public class TodoController : ControllerBase
{
    private readonly GapStore _store;
    private readonly TodoStore _todo;
    private readonly TodoOwner _owner;
    private readonly LibraryVerifier _verifier;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoController"/> class.
    /// </summary>
    /// <param name="store">The gap store, to snapshot the report when adding entries.</param>
    /// <param name="todo">The per-user todo-list store.</param>
    /// <param name="owner">Resolves whose list a request is for.</param>
    /// <param name="verifier">The library verifier, so a todo entry is checked exactly as a report row is.</param>
    public TodoController(GapStore store, TodoStore todo, TodoOwner owner, LibraryVerifier verifier)
    {
        _store = store;
        _todo = todo;
        _owner = owner;
        _verifier = verifier;
    }

    /// <summary>
    /// Gets the personal todo list (gaps the user marked to acquire), with the web-search URL template the
    /// dashboard builds each row's search link from.
    /// </summary>
    /// <returns>The todo list.</returns>
    [HttpGet("Todo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TodoList> GetTodo()
    {
        if (!TryOwner(out var userId))
        {
            return Forbid();
        }

        var config = Plugin.Instance?.Configuration;
        return new TodoList
        {
            Items = _todo.Load(userId),
            SearchUrlTemplate = config?.SearchUrlTemplate ?? new PluginConfiguration().SearchUrlTemplate,
            GeneratedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Gets every user's todo list, so an administrator can see what each user has added and manage it. The
    /// caller's own list is always among the owners, first; the rest follow by name.
    /// </summary>
    /// <returns>The owners and all their entries.</returns>
    [HttpGet("Todo/All")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TodoEveryone> GetEveryonesTodo()
    {
        if (!TryOwner(out var caller))
        {
            return Forbid();
        }

        var (owners, items) = LoadEveryonesEntries(caller);
        var config = Plugin.Instance?.Configuration;
        return new TodoEveryone
        {
            CallerId = caller,
            Owners = owners.OrderBy(o => o.UserId == caller ? 0 : 1).ThenBy(o => o.UserName, StringComparer.OrdinalIgnoreCase).ToList(),
            Items = items,
            SearchUrlTemplate = config?.SearchUrlTemplate ?? new PluginConfiguration().SearchUrlTemplate,
            GeneratedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Gets the fulfillment queue: every title on any user's todo list, folded to one row per title (the same
    /// title added from two different gaps still folds into one row) and sorted by outstanding demand. Built
    /// for an administrator who fetches titles by hand (no Radarr/Sonarr/Jellyseerr configured) and wants to
    /// know what is actually wanted across every user before going to find it.
    /// </summary>
    /// <returns>The queue.</returns>
    [HttpGet("Todo/Demand")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TodoDemandList> GetTodoDemand()
    {
        if (!TryOwner(out var caller))
        {
            return Forbid();
        }

        var (_, items) = LoadEveryonesEntries(caller);
        var config = Plugin.Instance?.Configuration;
        return new TodoDemandList
        {
            Items = TodoDemandAggregator.Build(items),
            SearchUrlTemplate = config?.SearchUrlTemplate ?? new PluginConfiguration().SearchUrlTemplate,
            GeneratedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Marks a fulfillment queue row fetched: sets every named requester's entry done in one call, for when
    /// the administrator found the title themselves (through StreamFab or otherwise) rather than the library
    /// picking it up on its own. An entry whose user or id no longer exists is skipped, not an error.
    /// </summary>
    /// <param name="entries">The row's member entries, from <see cref="TodoDemandRow.Entries"/>.</param>
    /// <returns>How many entries were named and how many were actually found and flipped.</returns>
    [HttpPost("Todo/Demand/MarkFetched")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TodoMarkFetchedResult> MarkFetched([FromBody] IReadOnlyList<TodoDemandEntryRef> entries)
    {
        if (!TryOwner(out _))
        {
            return Forbid();
        }

        var refs = entries ?? [];
        var updated = refs.Count(r => _owner.Exists(r.OwnerId) && _todo.SetDone(r.OwnerId, r.GapId, true));
        return new TodoMarkFetchedResult { Requested = refs.Count, Updated = updated };
    }

    // The flattened entries behind both Todo/All and Todo/Demand: every existing owner's list plus the
    // caller's own (added even when empty, so an administrator with nothing on their own list still sees it
    // among the owners), each entry tagged with its owner.
    private (List<TodoOwnerSummary> Owners, List<OwnedTodoEntry> Items) LoadEveryonesEntries(Guid caller)
    {
        var ids = _todo.ListOwners().Where(_owner.Exists).ToList();
        if (!ids.Contains(caller))
        {
            ids.Add(caller);
        }

        var owners = new List<TodoOwnerSummary>();
        var items = new List<OwnedTodoEntry>();
        foreach (var id in ids)
        {
            var name = _owner.NameOf(id);
            var entries = _todo.Load(id);
            owners.Add(new TodoOwnerSummary { UserId = id, UserName = name, Count = entries.Count, Open = entries.Count(e => !e.Done) });
            items.AddRange(entries.Select(e => OwnedTodoEntry.From(e, id, name)));
        }

        return (owners, items);
    }

    /// <summary>
    /// Adds the named report gaps to the personal todo list, snapshotting each server-side from the stored
    /// report by id (never trusting a client-posted gap body). Unknown ids are dropped; re-adding a title
    /// keeps its existing done state.
    /// </summary>
    /// <param name="ids">The stable ids of the report gaps to add.</param>
    /// <returns>The number of entries newly added.</returns>
    [HttpPost("Todo/Add")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<int> AddTodo([FromBody] IReadOnlyList<string> ids)
    {
        if (!TryOwner(out var userId))
        {
            return Forbid();
        }

        var wanted = new HashSet<string>(ids ?? [], StringComparer.Ordinal);
        var gaps = _store.LoadSnapshot().Items.Where(i => wanted.Contains(i.Id)).ToList();
        return _todo.Add(userId, gaps);
    }

    /// <summary>
    /// Removes an entry from a todo list: the caller's own, or another user's when an administrator names one.
    /// </summary>
    /// <param name="id">The entry id.</param>
    /// <param name="userId">The user whose list it is; omitted means the caller's own.</param>
    /// <returns>The number of entries removed (0 or 1).</returns>
    [HttpPost("Todo/Remove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<int> RemoveTodo([FromQuery] string id, [FromQuery] Guid? userId = null)
    {
        if (Target(userId, out var target) is { } refusal)
        {
            return refusal;
        }

        return _todo.Remove(target, id);
    }

    /// <summary>
    /// Sets a todo entry's done state, on the caller's own list or another user's.
    /// </summary>
    /// <param name="id">The entry id.</param>
    /// <param name="done">Whether the entry is done.</param>
    /// <param name="userId">The user whose list it is; omitted means the caller's own.</param>
    /// <returns>No content.</returns>
    [HttpPost("Todo/SetDone")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult SetTodoDone([FromQuery] string id, [FromQuery] bool done, [FromQuery] Guid? userId = null)
    {
        if (Target(userId, out var target) is { } refusal)
        {
            return refusal;
        }

        _todo.SetDone(target, id, done);
        return NoContent();
    }

    /// <summary>
    /// Verifies a todo entry against the library: whether a real (non-virtual) item of the entry's kind now
    /// carries any of its provider ids. Marks the entry done to match, and returns the outcome with the
    /// updated entry.
    /// </summary>
    /// <param name="id">The entry id.</param>
    /// <param name="userId">The user whose list it is; omitted means the caller's own.</param>
    /// <returns>Whether the library owns the entry, and the entry with its done state updated.</returns>
    [HttpPost("Todo/Verify")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TodoVerifyResult> VerifyTodo([FromQuery] string id, [FromQuery] Guid? userId = null)
    {
        if (Target(userId, out var target) is { } refusal)
        {
            return refusal;
        }

        var entry = _todo.Load(target).FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
        if (entry is null)
        {
            return new TodoVerifyResult { Owned = false, Entry = null };
        }

        var owned = LibraryOwns(entry);
        _todo.SetDone(target, entry.Id, owned);
        entry.Done = owned;

        // Reload so the returned entry carries the freshly stamped/cleared done timestamp.
        var updated = _todo.Load(target).FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal)) ?? entry;
        return new TodoVerifyResult { Owned = owned, Entry = updated };
    }

    /// <summary>
    /// Verifies the whole todo list against the library in one pass, marking each entry done or not to match.
    /// The bulk form of <see cref="VerifyTodo"/>, for the popup's "check everything" action and for the
    /// verify the Markdown export runs before writing, so an exported checklist is true when it is written.
    /// </summary>
    /// <param name="userId">The user whose list it is; omitted means the caller's own.</param>
    /// <returns>How many entries were checked and how many the library now holds, with the updated list.</returns>
    [HttpPost("Todo/VerifyAll")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TodoVerifyAllResult> VerifyAllTodo([FromQuery] Guid? userId = null)
    {
        if (Target(userId, out var target) is { } refusal)
        {
            return refusal;
        }

        var entries = _todo.Load(target);
        var owned = 0;

        // Stamp both ways: an entry marked done whose file has since left the library becomes outstanding
        // again, so the list keeps telling the truth rather than only ever accumulating ticks. Collected
        // first and applied in one write, since the store flushes the whole file per change.
        var states = _verifier.OwnedAmong(entries);
        foreach (var state in states)
        {
            if (state.Value)
            {
                owned++;
            }
        }

        _todo.ReconcileDone(target, states);

        return new TodoVerifyAllResult
        {
            Checked = entries.Count,
            Owned = owned,
            Items = _todo.Load(target)
        };
    }

    // The list an action is for: the caller's own, or another user's when one is named. Every action here needs
    // an administrator, so naming another user is allowed; naming one that does not exist is not, which also
    // keeps a made-up id from creating a list. Null when the action may go ahead.
    private ActionResult? Target(Guid? requested, out Guid userId)
    {
        userId = Guid.Empty;
        if (!TryOwner(out var caller))
        {
            return Forbid();
        }

        userId = requested ?? caller;
        return userId != caller && !_owner.Exists(userId) ? NotFound() : null;
    }

    // The caller's own list. An administrator's first call after an upgrade takes over the old server-wide list.
    // False for a request that is not a signed-in user's (an API key), which has no list.
    private bool TryOwner(out Guid userId)
    {
        var owner = _owner.Resolve(User, User.IsInRole("Administrator"));
        userId = owner ?? Guid.Empty;
        return owner is not null;
    }

    // Whether the library holds this entry, decided by the same rules the report's verify uses (a shared
    // provider id, or for an album the artist-and-title name match). A todo entry is a gap the user copied
    // aside, so the two must not be able to disagree about whether it has been filled.
    private bool LibraryOwns(TodoEntry entry)
        => Enum.TryParse<BaseItemKind>(entry.TargetKindName, ignoreCase: false, out var kind)
            && _verifier.Owns(kind, entry.ProviderIds, entry.Creator, entry.Name);
}
