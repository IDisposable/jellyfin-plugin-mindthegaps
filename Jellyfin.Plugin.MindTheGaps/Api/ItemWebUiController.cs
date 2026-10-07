using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

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
    private readonly WatchlistPlaylistService _playlist;
    private readonly ILibraryManager _library;
    private readonly ILogger<ItemWebUiController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemWebUiController"/> class.
    /// </summary>
    /// <param name="related">Computes a title's unowned similar titles.</param>
    /// <param name="works">Computes an artist's unowned albums and a book's unowned works by its author.</param>
    /// <param name="todo">The per-user todo-list store, for the "Add to TODO" action.</param>
    /// <param name="access">Decides what the signed-in user may be shown.</param>
    /// <param name="playlist">The want-to-watch playlist, for a movie or series page's bookmark.</param>
    /// <param name="library">The library manager, to tell what kind of item a page is about.</param>
    /// <param name="logger">The logger.</param>
    public ItemWebUiController(RelatedMissingService related, WorksMissingService works, TodoStore todo, WebUiAccess access, WatchlistPlaylistService playlist, ILibraryManager library, ILogger<ItemWebUiController> logger)
        : base(todo, access)
    {
        _related = related;
        _works = works;
        _playlist = playlist;
        _library = library;
        _logger = logger;
    }

    private static bool ItemPageEnabled => WebUiGate.ItemPage(Plugin.Instance?.Configuration);

    /// <summary>
    /// Puts this owned movie or series on the caller's want-to-watch playlist.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <returns>1 when added, 0 when it was already there, or 404 as for <see cref="GetItemWantedOrRelated"/>'s bookmark.</returns>
    [HttpPost("Item/{itemId}/Wanted")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<int>> AddItemWanted([FromRoute] Guid itemId)
    {
        if (BookmarkingUser(itemId) is not { } userId)
        {
            return NotFound();
        }

        return await _playlist.AddItemAsync(userId, itemId, Plugin.Instance?.Configuration).ConfigureAwait(false) ? 1 : 0;
    }

    /// <summary>
    /// Takes this owned movie or series off the caller's want-to-watch playlist.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <returns>1 when removed, 0 when it was not there, or 404 as for <see cref="GetItemWantedOrRelated"/>'s bookmark.</returns>
    [HttpPost("Item/{itemId}/Wanted/Remove")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<int>> RemoveItemWanted([FromRoute] Guid itemId)
    {
        if (BookmarkingUser(itemId) is not { } userId)
        {
            return NotFound();
        }

        return await _playlist.RemoveTitleAsync(userId, itemId, Plugin.Instance?.Configuration).ConfigureAwait(false) ? 1 : 0;
    }

    // The caller, when the page bookmark is on, they may keep a list, and the item is a movie or series they
    // may see; otherwise null.
    private Guid? BookmarkingUser(Guid itemId)
    {
        if (!WebUiGate.DetailBookmark(Plugin.Instance?.Configuration)
            || !WatchlistPlaylistService.IsWantable(_library.GetItemById(itemId))
            || !Access.MaySee(User, itemId))
        {
            return null;
        }

        return Wanting().UserId;
    }

    /// <summary>
    /// What this owned movie or series page shows: whether it is on the caller's want-to-watch playlist, for
    /// its bookmark, and the similar titles the library does not hold. Each part answers on its own toggle.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parts that apply, or 404 when neither does.</returns>
    [HttpGet("Item/{itemId}/WantedOrRelated")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WantedOrRelatedResult>> GetItemWantedOrRelated([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        var result = new WantedOrRelatedResult();
        if (BookmarkingUser(itemId) is { } userId)
        {
            result.OnList = _playlist.Contains(userId, itemId, config);
        }

        if (ItemPageEnabled && Access.MaySee(User, itemId))
        {
            try
            {
                result.Related = await _related.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The bookmark still stands without the similar titles.
                _logger.LogWarning(ex, "Could not look up the titles related to item {ItemId}", itemId);
            }
        }

        if (result.Related is { } related)
        {
            var (wantingUser, wanted) = Wanting();
            related.CanTodo = wantingUser is not null;
            related.Placement = ItemPlacement.Of(config);
            WantedMarker.Mark(related.Titles, wanted);
        }

        return result.OnList is null && result.Related is null ? NotFound() : result;
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
        result.Placement = ItemPlacement.Of(Plugin.Instance?.Configuration);
        return result;
    }

    /// <summary>
    /// Fetches the richer detail for one of an artist's or author's unowned works, once its dialog opens:
    /// today, a book's description. An album gap answers with nothing extra (see
    /// <see cref="MissingWorkDetail"/>).
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The detail, or 404 for a gap that is not there or while the surface is off.</returns>
    [HttpGet("Item/{itemId}/Works/Detail")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MissingWorkDetail>> GetItemWorkDetail([FromRoute] Guid itemId, [FromQuery] string? gapId, CancellationToken cancellationToken)
    {
        if (!ItemPageEnabled || !Access.MaySee(User, itemId) || string.IsNullOrEmpty(gapId))
        {
            return NotFound();
        }

        var detail = await _works.GetDetailAsync(itemId, gapId, cancellationToken).ConfigureAwait(false);
        return detail is null ? NotFound() : detail;
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
