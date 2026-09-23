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
/// The studio-list-page web UI surface: an owned studio's unowned movies, behind jellyfin-web's own generic
/// list page (routed by <c>studioId</c>, not a page this plugin injects a section into), plus a signed-in
/// user's todo-list add. Every endpoint answers 404 while the surface is off, so a toggle takes effect on
/// the next page load without a restart.
/// </summary>
[ApiController]
[Route("MindTheGaps")]
public class StudioWebUiController : WebUiControllerBase
{
    private readonly StudioMissingService _studios;

    /// <summary>
    /// Initializes a new instance of the <see cref="StudioWebUiController"/> class.
    /// </summary>
    /// <param name="studios">Computes a studio's unowned movies.</param>
    /// <param name="todo">The per-user todo-list store, for the "Add to TODO" action.</param>
    /// <param name="access">Decides what the signed-in user may be shown.</param>
    public StudioWebUiController(StudioMissingService studios, TodoStore todo, WebUiAccess access)
        : base(todo, access)
    {
        _studios = studios;
    }

    private static bool StudioPageEnabled => WebUiGate.StudioPage(Plugin.Instance?.Configuration);

    /// <summary>
    /// Lists the movies from this studio that the library does not hold.
    /// </summary>
    /// <param name="studioId">The Jellyfin studio item id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list, or 404 for an id that is not a library studio or while the surface is off.</returns>
    [HttpGet("Studio/{studioId}/Missing")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StudioMissingResult>> GetStudioMissing([FromRoute] Guid studioId, CancellationToken cancellationToken)
    {
        if (!StudioPageEnabled || !Access.MaySee(User, studioId))
        {
            return NotFound();
        }

        var result = await _studios.GetAsync(studioId, cancellationToken).ConfigureAwait(false);
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
    /// Adds one of a studio's unowned movies to the caller's personal todo list, rehydrated server-side from
    /// the same lookup the page listed.
    /// </summary>
    /// <param name="studioId">The Jellyfin studio item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries added (0 or 1), or 404 while the surface is off.</returns>
    [HttpPost("Studio/{studioId}/Todo")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> AddStudioGapToTodo([FromRoute] Guid studioId, [FromQuery] string? gapId, CancellationToken cancellationToken)
        => WantOwnedGapAsync(StudioPageEnabled, studioId, gapId, ct => _studios.FindGapAsync(studioId, gapId ?? string.Empty, ct), add: true, cancellationToken);

    /// <summary>
    /// Takes one of a studio's unowned movies off the caller's want-to-watch list.
    /// </summary>
    /// <param name="studioId">The Jellyfin studio item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries removed, or 404 while the surface is off.</returns>
    [HttpPost("Studio/{studioId}/Todo/Remove")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> RemoveStudioGapFromTodo([FromRoute] Guid studioId, [FromQuery] string? gapId, CancellationToken cancellationToken)
        => WantOwnedGapAsync(StudioPageEnabled, studioId, gapId, ct => _studios.FindGapAsync(studioId, gapId ?? string.Empty, ct), add: false, cancellationToken);
}
