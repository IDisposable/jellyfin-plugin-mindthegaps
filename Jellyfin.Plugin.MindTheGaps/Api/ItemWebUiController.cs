using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// The item-page web UI surface: an owned movie or series' unowned similar titles ("related"), and an
/// owned artist's/book author's unowned other works ("works"), plus a signed-in user's todo-list add on
/// each. Every endpoint answers 404 while the surface is off, so a toggle takes effect on the next page
/// load without a restart.
/// </summary>
[ApiController]
[Route("MindTheGaps")]
public class ItemWebUiController : WebUiControllerBase
{
    private readonly RelatedMissingService _related;
    private readonly WorksMissingService _works;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemWebUiController"/> class.
    /// </summary>
    /// <param name="related">Computes a title's unowned similar titles.</param>
    /// <param name="works">Computes an artist's unowned albums and a book's unowned works by its author.</param>
    /// <param name="todo">The per-user todo-list store, for the "Add to TODO" action.</param>
    /// <param name="access">Decides what the signed-in user may be shown.</param>
    public ItemWebUiController(RelatedMissingService related, WorksMissingService works, TodoStore todo, WebUiAccess access)
        : base(todo, access)
    {
        _related = related;
        _works = works;
    }

    private static bool ItemPageEnabled => WebUiGate.ItemPage(Plugin.Instance?.Configuration);

    /// <summary>
    /// Lists the titles similar to this owned movie or series that the library does not hold.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list, or 404 for an id that is not a library movie or series or while the surface is off.</returns>
    [HttpGet("Item/{itemId}/Related")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RelatedMissingResult>> GetItemRelated([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        if (!ItemPageEnabled || !Access.MaySee(User, itemId))
        {
            return NotFound();
        }

        var result = await _related.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        var (wantingUser, wanted) = Wanting();
        result.CanTodo = wantingUser is not null;
        WantedMarker.Mark(result.Titles, wanted);
        return result;
    }

    /// <summary>
    /// Adds one of an owned title's unowned similar titles to the caller's personal todo list, rehydrated
    /// server-side from the same lookup the page listed.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries added (0 or 1), or 404 while the surface is off.</returns>
    [HttpPost("Item/{itemId}/Todo")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> AddItemGapToTodo([FromRoute] Guid itemId, [FromQuery] string? gapId, CancellationToken cancellationToken)
        => WantOwnedGapAsync(ItemPageEnabled, itemId, gapId, ct => _related.FindGapAsync(itemId, gapId ?? string.Empty, ct), add: true, cancellationToken);

    /// <summary>
    /// Takes one of an owned title's unowned similar titles off the caller's want-to-watch list.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries removed, or 404 while the surface is off.</returns>
    [HttpPost("Item/{itemId}/Todo/Remove")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> RemoveItemGapFromTodo([FromRoute] Guid itemId, [FromQuery] string? gapId, CancellationToken cancellationToken)
        => WantOwnedGapAsync(ItemPageEnabled, itemId, gapId, ct => _related.FindGapAsync(itemId, gapId ?? string.Empty, ct), add: false, cancellationToken);

    /// <summary>
    /// Lists the albums an owned artist made, or the other works by an owned book's author, that the library
    /// does not hold.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list, or 404 for an id that is not an artist or book, when no enabled source handles it,
    /// or while the item page surface is off.</returns>
    [HttpGet("Item/{itemId}/Works")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorksMissingResult>> GetItemWorks([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        if (!ItemPageEnabled || !Access.MaySee(User, itemId))
        {
            return NotFound();
        }

        var (wantingUser, wanted) = Wanting();
        var result = await _works.GetAsync(itemId, wanted, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        result.CanTodo = wantingUser is not null;
        return result;
    }

    /// <summary>
    /// Adds one of an artist's or author's unowned works to the caller's personal todo list, rehydrated
    /// server-side from the same lookup the page listed.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries added (0 or 1), or 404 while the surface is off.</returns>
    [HttpPost("Item/{itemId}/Works/Todo")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> AddItemWorkToTodo([FromRoute] Guid itemId, [FromQuery] string? gapId, CancellationToken cancellationToken)
        => WantOwnedGapAsync(ItemPageEnabled, itemId, gapId, ct => _works.FindGapAsync(itemId, gapId ?? string.Empty, ct), add: true, cancellationToken);

    /// <summary>
    /// Takes one of an artist's or author's unowned works off the caller's want-to-watch list.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries removed, or 404 while the surface is off.</returns>
    [HttpPost("Item/{itemId}/Works/Todo/Remove")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> RemoveItemWorkFromTodo([FromRoute] Guid itemId, [FromQuery] string? gapId, CancellationToken cancellationToken)
        => WantOwnedGapAsync(ItemPageEnabled, itemId, gapId, ct => _works.FindGapAsync(itemId, gapId ?? string.Empty, ct), add: false, cancellationToken);
}
