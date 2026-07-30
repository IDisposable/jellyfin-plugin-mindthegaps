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
    private readonly LibraryVerifier _verifier;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoController"/> class.
    /// </summary>
    /// <param name="store">The gap store, to snapshot the report when adding entries.</param>
    /// <param name="todo">The personal todo-list store.</param>
    /// <param name="verifier">The library verifier, so a todo entry is checked exactly as a report row is.</param>
    public TodoController(GapStore store, TodoStore todo, LibraryVerifier verifier)
    {
        _store = store;
        _todo = todo;
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

    /// <summary>
    /// Verifies the whole todo list against the library in one pass, marking each entry done or not to match.
    /// The bulk form of <see cref="VerifyTodo"/>, for the popup's "check everything" action and for the
    /// verify the Markdown export runs before writing, so an exported checklist is true when it is written.
    /// </summary>
    /// <returns>How many entries were checked and how many the library now holds, with the updated list.</returns>
    [HttpPost("Todo/VerifyAll")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TodoVerifyAllResult> VerifyAllTodo()
    {
        var entries = _todo.Load();
        var owned = 0;

        // Stamp both ways: an entry marked done whose file has since left the library becomes outstanding
        // again, so the list keeps telling the truth rather than only ever accumulating ticks. Collected
        // first and applied in one write, since the store flushes the whole file per change.
        var states = new Dictionary<string, bool>(entries.Count, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var has = LibraryOwns(entry);
            if (has)
            {
                owned++;
            }

            states[entry.Id] = has;
        }

        _todo.ReconcileDone(states);

        return new TodoVerifyAllResult
        {
            Checked = entries.Count,
            Owned = owned,
            Items = _todo.Load()
        };
    }

    // Whether the library holds this entry, decided by the same rules the report's verify uses (a shared
    // provider id, or for an album the artist-and-title name match). A todo entry is a gap the user copied
    // aside, so the two must not be able to disagree about whether it has been filled.
    private bool LibraryOwns(TodoEntry entry)
        => Enum.TryParse<BaseItemKind>(entry.TargetKindName, ignoreCase: false, out var kind)
            && _verifier.Owns(kind, entry.ProviderIds, entry.Creator, entry.Name);
}
