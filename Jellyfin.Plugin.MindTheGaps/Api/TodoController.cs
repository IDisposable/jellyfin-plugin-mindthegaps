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
/// Endpoints for the personal todo list (gaps the user marked to acquire), including verifying an entry
/// against the library. Shares the <c>MindTheGaps</c> route.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("MindTheGaps")]
[Produces("application/json")]
public class TodoController : ControllerBase
{
    private readonly GapStore _store;
    private readonly TodoStore _todo;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoController"/> class.
    /// </summary>
    /// <param name="store">The gap store, to snapshot the report when adding entries.</param>
    /// <param name="todo">The personal todo-list store.</param>
    /// <param name="libraryManager">The library manager, used to verify a todo entry against the library.</param>
    public TodoController(GapStore store, TodoStore todo, ILibraryManager libraryManager)
    {
        _store = store;
        _todo = todo;
        _libraryManager = libraryManager;
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
        var config = Plugin.Instance?.Configuration;
        return new TodoList
        {
            Items = _todo.Load(),
            SearchUrlTemplate = config?.SearchUrlTemplate ?? new PluginConfiguration().SearchUrlTemplate,
            GeneratedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };
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
        var wanted = new HashSet<string>(ids ?? [], StringComparer.Ordinal);
        var gaps = _store.LoadSnapshot().Items.Where(i => wanted.Contains(i.Id)).ToList();
        return _todo.Add(gaps);
    }

    /// <summary>
    /// Removes an entry from the personal todo list.
    /// </summary>
    /// <param name="id">The entry id.</param>
    /// <returns>The number of entries removed (0 or 1).</returns>
    [HttpPost("Todo/Remove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<int> RemoveTodo([FromQuery] string id) => _todo.Remove(id);

    /// <summary>
    /// Sets a todo entry's done state.
    /// </summary>
    /// <param name="id">The entry id.</param>
    /// <param name="done">Whether the entry is done.</param>
    /// <returns>No content.</returns>
    [HttpPost("Todo/SetDone")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult SetTodoDone([FromQuery] string id, [FromQuery] bool done)
    {
        _todo.SetDone(id, done);
        return NoContent();
    }

    /// <summary>
    /// Verifies a todo entry against the library: whether a real (non-virtual) item of the entry's kind now
    /// carries any of its provider ids. Marks the entry done to match, and returns the outcome with the
    /// updated entry.
    /// </summary>
    /// <param name="id">The entry id.</param>
    /// <returns>Whether the library owns the entry, and the entry with its done state updated.</returns>
    [HttpPost("Todo/Verify")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TodoVerifyResult> VerifyTodo([FromQuery] string id)
    {
        var entry = _todo.Load().FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
        if (entry is null)
        {
            return new TodoVerifyResult { Owned = false, Entry = null };
        }

        var owned = LibraryOwns(entry);
        _todo.SetDone(entry.Id, owned);
        entry.Done = owned;

        // Reload so the returned entry carries the freshly stamped/cleared done timestamp.
        var updated = _todo.Load().FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal)) ?? entry;
        return new TodoVerifyResult { Owned = owned, Entry = updated };
    }

    // Whether the library owns a real (non-virtual) item of the entry's kind carrying any of its provider
    // ids. A focused query (the kind, real items only, any of the ids) keeps the lookup cheap rather than
    // building a whole ownership index for a single check.
    private bool LibraryOwns(TodoEntry entry)
    {
        if (entry.ProviderIds.Count == 0
            || !Enum.TryParse<BaseItemKind>(entry.TargetKindName, ignoreCase: false, out var kind))
        {
            return false;
        }

        var hasAny = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in entry.ProviderIds)
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                hasAny[pair.Key] = pair.Value;
            }
        }

        if (hasAny.Count == 0)
        {
            return false;
        }

        var matches = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { kind },
            IsVirtualItem = false,
            HasAnyProviderId = hasAny,
            Recursive = true
        });

        return matches.Count > 0;
    }
}
