using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// The person-page web UI surface: a person's unowned filmography, plus an administrator's Send and a
/// signed-in user's todo-list add on each. Every endpoint answers 404 while the surface is off, so a
/// toggle takes effect on the next page load without a restart.
/// </summary>
[ApiController]
[Route("MindTheGaps")]
public class PersonWebUiController : WebUiControllerBase
{
    private readonly PersonMissingService _person;

    /// <summary>
    /// Initializes a new instance of the <see cref="PersonWebUiController"/> class.
    /// </summary>
    /// <param name="person">Computes a person's unowned filmography.</param>
    /// <param name="acquisition">The acquisition handoff service (Radarr/Sonarr).</param>
    /// <param name="todo">The per-user todo-list store, for the "Add to TODO" fallback when no arr is set up.</param>
    /// <param name="access">Decides what the signed-in user may be shown.</param>
    public PersonWebUiController(PersonMissingService person, AcquisitionService acquisition, TodoStore todo, WebUiAccess access)
        : base(acquisition, todo, access)
    {
        _person = person;
    }

    private static bool PersonPageEnabled => WebUiGate.PersonPage(Plugin.Instance?.Configuration);

    /// <summary>
    /// Lists the movies and series this person is credited on that the library does not hold.
    /// </summary>
    /// <param name="personId">The Jellyfin person id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lists, or 404 for an id that is not a library person or while the surface is off.</returns>
    [HttpGet("Person/{personId}/Missing")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonMissingResult>> GetPersonMissing([FromRoute] Guid personId, CancellationToken cancellationToken)
    {
        if (!PersonPageEnabled || !Access.MaySee(User, personId))
        {
            return NotFound();
        }

        var result = await _person.GetAsync(personId, IsAdministrator, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        var (wantingUser, wanted) = Wanting();
        result.CanTodo = wantingUser is not null;
        WantedMarker.Mark(result.Movies.Concat(result.Series), wanted);
        return result;
    }

    /// <summary>
    /// Sends one of a person's unowned credits to Radarr or Sonarr, rehydrated server-side from the same
    /// filmography lookup the page listed, so a client can only ever send a title this person is actually
    /// credited on and the library actually lacks.
    /// </summary>
    /// <param name="personId">The Jellyfin person id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="qualityProfileId">Overrides the configured default quality profile, from the dialog's
    /// picker; omitted uses the configured default.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome, or 404 while the surface is off.</returns>
    [HttpPost("Person/{personId}/Send")]
    [Authorize(Policy = "RequiresElevation")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<AcquisitionSendResult>> SendPersonGap([FromRoute] Guid personId, [FromQuery] string? gapId, [FromQuery] int? qualityProfileId, CancellationToken cancellationToken)
        => SendOwnedGapAsync(
            PersonPageEnabled,
            ct => _person.FindGapAsync(personId, gapId ?? string.Empty, ct),
            qualityProfileId,
            "That title is no longer listed for this person; refresh the page and try again.",
            cancellationToken);

    /// <summary>
    /// Adds one of a person's unowned credits to the caller's personal todo list, rehydrated server-side the
    /// same way a Send is. The fallback for an administrator who has not set up Radarr/Sonarr yet: a title
    /// the scan rotation has not reached this person for yet still works, since this never reads the
    /// persisted report the way <c>Todo/Add</c> does.
    /// </summary>
    /// <param name="personId">The Jellyfin person id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries added (0 or 1), or 404 while the surface is off.</returns>
    [HttpPost("Person/{personId}/Todo")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> AddPersonGapToTodo([FromRoute] Guid personId, [FromQuery] string? gapId, CancellationToken cancellationToken)
        => WantOwnedGapAsync(PersonPageEnabled, personId, gapId, ct => _person.FindGapAsync(personId, gapId ?? string.Empty, ct), add: true, cancellationToken);

    /// <summary>
    /// Takes one of a person's unowned credits off the caller's want-to-watch list, by the title the credit is
    /// about, wherever the entry came from.
    /// </summary>
    /// <param name="personId">The Jellyfin person id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries removed, or 404 while the surface is off.</returns>
    [HttpPost("Person/{personId}/Todo/Remove")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> RemovePersonGapFromTodo([FromRoute] Guid personId, [FromQuery] string? gapId, CancellationToken cancellationToken)
        => WantOwnedGapAsync(PersonPageEnabled, personId, gapId, ct => _person.FindGapAsync(personId, gapId ?? string.Empty, ct), add: false, cancellationToken);
}
