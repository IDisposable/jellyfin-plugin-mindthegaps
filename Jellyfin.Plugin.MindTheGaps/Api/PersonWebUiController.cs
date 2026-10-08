using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// The person-page web UI surface: a person's unowned filmography, plus a signed-in user's todo-list add on
/// each. Every endpoint answers 404 while the surface is off, so a change takes effect on the next page
/// load without a restart.
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
    /// <param name="todo">The per-user todo-list store, for the "Add to TODO" action.</param>
    /// <param name="notInterested">The per-user store of titles the user is not interested in.</param>
    /// <param name="access">Decides what the signed-in user may be shown.</param>
    /// <param name="certifications">Narrows a restricted user's titles to their parental rating limit.</param>
    public PersonWebUiController(PersonMissingService person, TodoStore todo, NotInterestedStore notInterested, WebUiAccess access, CertificationFilter certifications)
        : base(todo, notInterested, access, certifications)
    {
        _person = person;
    }

    private static bool PersonPageShown => WebUiGate.PersonPage(Plugin.Instance?.Configuration);

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
        if (!PersonPageShown || !Access.MaySee(User, personId))
        {
            return NotFound();
        }

        var result = await _person.GetAsync(personId, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        // One pass over both lists rather than one per list: a restricted caller's certification lookups run a
        // few at a time, and two passes would leave those slots idle while the movies' slowest lookups finish.
        // Concurrent passes would instead double how many reach TMDB at once.
        // A card's Kind is the kind PersonMissingBuilder.Split filed it under, so it says which list it goes back to.
        var (notInterestedUser, notInterested, notInterestedCount) = NotInterested();
        var allowed = await ForCallerAsync(NotInterestedFilter.Without([.. result.Movies, .. result.Series], notInterested), cancellationToken).ConfigureAwait(false);
        var movies = new List<MissingTitle>(allowed.Count);
        var series = new List<MissingTitle>();
        foreach (var title in allowed)
        {
            (string.Equals(title.Kind, "Movie", StringComparison.Ordinal) ? movies : series).Add(title);
        }

        result.Movies = movies;
        result.Series = series;
        var (wantingUser, wanted) = Wanting();
        result.CanTodo = wantingUser is not null;
        result.CanHide = notInterestedUser is not null;
        result.NotInterestedCount = notInterestedCount;
        WantedMarker.Mark(result.Movies.Concat(result.Series), wanted);
        return result;
    }

    /// <summary>
    /// Adds one of a person's unowned credits to the caller's personal todo list, rehydrated server-side
    /// from the same filmography lookup the page listed, so a client can only ever add a title this person
    /// is actually credited on and the library actually lacks.
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
        => WantOwnedGapAsync(PersonPageShown, personId, gapId, ct => _person.FindGapAsync(personId, gapId ?? string.Empty, ct), add: true, cancellationToken);

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
        => WantOwnedGapAsync(PersonPageShown, personId, gapId, ct => _person.FindGapAsync(personId, gapId ?? string.Empty, ct), add: false, cancellationToken);

    /// <summary>
    /// Says the caller is not interested in one of a person's unowned credits, so no surface shows it to them.
    /// </summary>
    /// <param name="personId">The Jellyfin person id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of titles added (0 or 1), or 404 while the surface or the feature is off.</returns>
    [HttpPost("Person/{personId}/NotInterested")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> AddPersonGapNotInterested([FromRoute] Guid personId, [FromQuery] string? gapId, CancellationToken cancellationToken)
        => NotInterestedGapAsync(PersonPageShown, personId, gapId, ct => _person.FindGapAsync(personId, gapId ?? string.Empty, ct), add: true, cancellationToken);

    /// <summary>
    /// Takes back the caller's "not interested" on one of a person's unowned credits.
    /// </summary>
    /// <param name="personId">The Jellyfin person id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of titles removed, or 404 while the surface or the feature is off.</returns>
    [HttpPost("Person/{personId}/NotInterested/Remove")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> RemovePersonGapNotInterested([FromRoute] Guid personId, [FromQuery] string? gapId, CancellationToken cancellationToken)
        => NotInterestedGapAsync(PersonPageShown, personId, gapId, ct => _person.FindGapAsync(personId, gapId ?? string.Empty, ct), add: false, cancellationToken);
}
