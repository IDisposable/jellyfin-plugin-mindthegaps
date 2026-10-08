using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// The signed-in user's own list of titles they are not interested in, which every surface's row header opens
/// so a title can be shown again. Saying "not interested" happens on each surface, against the card's own
/// lookup; this only reads the list and takes titles off it. Always the caller's own list, an administrator's
/// included: there is no way here to read or change another user's.
/// </summary>
[ApiController]
[Route("MindTheGaps")]
public class NotInterestedWebUiController : WebUiControllerBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotInterestedWebUiController"/> class.
    /// </summary>
    /// <param name="todo">The per-user todo-list store.</param>
    /// <param name="notInterested">The per-user store of titles the user is not interested in.</param>
    /// <param name="access">Decides what the signed-in user may be shown.</param>
    /// <param name="certifications">Narrows a restricted user's titles to their parental rating limit.</param>
    public NotInterestedWebUiController(TodoStore todo, NotInterestedStore notInterested, WebUiAccess access, CertificationFilter certifications)
        : base(todo, notInterested, access, certifications)
    {
    }

    /// <summary>
    /// Lists the titles the caller is not interested in, most recently dismissed first.
    /// </summary>
    /// <returns>The list, or 404 while the feature is off or for a request that cannot keep one.</returns>
    [HttpGet("WebUi/NotInterested")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<NotInterestedEntry>> GetNotInterested()
    {
        if (NotInterested().UserId is not { } userId)
        {
            return NotFound();
        }

        return Ok(NotInterestedList.Load(userId));
    }

    /// <summary>
    /// Takes a title off the caller's list, so the surfaces show it to them again.
    /// </summary>
    /// <param name="id">The entry id the list showed.</param>
    /// <returns>The number of titles removed (0 or 1), or 404 while the feature is off or for a request that
    /// cannot keep a list.</returns>
    [HttpPost("WebUi/NotInterested/Restore")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<int> RestoreNotInterested([FromQuery] string? id)
    {
        if (NotInterested().UserId is not { } userId)
        {
            return NotFound();
        }

        return NotInterestedList.Remove(userId, id ?? string.Empty);
    }

    /// <summary>
    /// Empties the caller's list, so the surfaces show them every title again.
    /// </summary>
    /// <returns>The number of titles removed, or 404 while the feature is off or for a request that cannot keep a
    /// list.</returns>
    [HttpPost("WebUi/NotInterested/Clear")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<int> ClearNotInterested()
    {
        if (NotInterested().UserId is not { } userId)
        {
            return NotFound();
        }

        return NotInterestedList.Clear(userId);
    }
}
