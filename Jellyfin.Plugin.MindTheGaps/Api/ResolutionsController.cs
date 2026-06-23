using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// Endpoints for per-gap resolutions ("not really missing" notes that persist across rescans, ADR-0008).
/// Shares the <c>MindTheGaps</c> route.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("MindTheGaps")]
[Produces("application/json")]
public class ResolutionsController : ControllerBase
{
    private readonly ResolutionStore _resolutions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResolutionsController"/> class.
    /// </summary>
    /// <param name="resolutions">The store of per-gap resolution notes.</param>
    public ResolutionsController(ResolutionStore resolutions)
    {
        _resolutions = resolutions;
    }

    /// <summary>
    /// Gets every gap resolution (gaps marked as not really missing), keyed by gap id.
    /// </summary>
    /// <returns>The resolutions.</returns>
    [HttpGet("Resolutions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyDictionary<string, GapResolution>> GetResolutions() => Ok(_resolutions.GetAll());

    /// <summary>
    /// Marks a gap resolved (not really missing) with a note. Persists across rescans.
    /// </summary>
    /// <param name="request">The gap id and note.</param>
    /// <returns>No content.</returns>
    [HttpPost("Resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Resolve([FromBody] ResolveRequest request)
    {
        _resolutions.SetState(request.Id, request.Kind, request.Note, request.SnoozedUntil);
        return NoContent();
    }

    /// <summary>
    /// Dismisses several gaps at once with the same kind and note, for the "resolve every item under this
    /// series or season" group actions. Persists across rescans.
    /// </summary>
    /// <param name="request">The gap ids, the kind, and a note.</param>
    /// <returns>No content.</returns>
    [HttpPost("ResolveBatch")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult ResolveBatch([FromBody] ResolveBatchRequest request)
    {
        foreach (var id in request.Ids)
        {
            _resolutions.SetState(id, request.Kind, request.Note, null);
        }

        return NoContent();
    }

    /// <summary>
    /// Clears a gap's resolution so it shows as missing again.
    /// </summary>
    /// <param name="id">The gap id.</param>
    /// <returns>No content.</returns>
    [HttpPost("Unresolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Unresolve([FromQuery] string id)
    {
        _resolutions.Clear(id);
        return NoContent();
    }
}
