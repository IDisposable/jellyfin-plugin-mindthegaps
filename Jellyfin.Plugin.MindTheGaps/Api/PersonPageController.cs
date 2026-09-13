using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.PersonPage;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// The person page feature's endpoints: the client script the web UI loads, the unowned filmography of a
/// person for any signed-in user, and a Send for administrators. Every endpoint answers 404 while the feature
/// is off, so turning it off in settings takes effect on the next page load without a restart.
/// </summary>
[ApiController]
[Route("MindTheGaps")]
public class PersonPageController : ControllerBase
{
    /// <summary>
    /// The route of the client script, relative to the server root, as the injected script tag references it.
    /// </summary>
    public const string ClientScriptPath = "MindTheGaps/PersonPage/client.js";

    private const string ClientScriptResource = "Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.personpage.js";
    private const string AdministratorRole = "Administrator";

    private readonly PersonMissingService _missing;
    private readonly AcquisitionService _acquisition;

    /// <summary>
    /// Initializes a new instance of the <see cref="PersonPageController"/> class.
    /// </summary>
    /// <param name="missing">Computes a person's unowned filmography.</param>
    /// <param name="acquisition">The acquisition handoff service (Radarr/Sonarr).</param>
    public PersonPageController(PersonMissingService missing, AcquisitionService acquisition)
    {
        _missing = missing;
        _acquisition = acquisition;
    }

    private static bool Enabled => Plugin.Instance?.Configuration.PersonPageEnabled == true;

    /// <summary>
    /// Serves the client script that renders the Missing section on person pages. Anonymous because
    /// index.html loads it before sign-in; it carries no data and calls the authenticated endpoints below
    /// through the web client's own session.
    /// </summary>
    /// <returns>The script, or 404 while the feature is off.</returns>
    [HttpGet("PersonPage/client.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetClientScript()
    {
        if (!Enabled)
        {
            return NotFound();
        }

        var assembly = typeof(PersonPageController).Assembly;
        var stream = assembly.GetManifestResourceStream(ClientScriptResource);
        if (stream is null)
        {
            return NotFound();
        }

        // Revalidate on every load (a plugin update must not be masked by a cached copy) but answer 304 cheaply.
        var version = assembly.GetName().Version?.ToString() ?? "0";
        var etag = new EntityTagHeaderValue(string.Create(CultureInfo.InvariantCulture, $"\"mtg-personpage-{version}\""));
        Response.Headers[HeaderNames.CacheControl] = "no-cache";
        return File(stream, "application/javascript", lastModified: null, entityTag: etag);
    }

    /// <summary>
    /// Lists the movies and series this person is credited on that the library does not hold.
    /// </summary>
    /// <param name="personId">The Jellyfin person id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lists, or 404 for an id that is not a library person or while the feature is off.</returns>
    [HttpGet("Person/{personId}/Missing")]
    [Authorize(Policy = "DefaultAuthorization")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonMissingResult>> GetMissing([FromRoute] Guid personId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return NotFound();
        }

        var result = await _missing.GetAsync(personId, User.IsInRole(AdministratorRole), cancellationToken).ConfigureAwait(false);
        return result is null ? NotFound() : result;
    }

    /// <summary>
    /// Sends one of the person's unowned titles to Radarr (a movie) or Sonarr (a series). The gap is
    /// recomputed server-side from the person's credits, never trusted from the client.
    /// </summary>
    /// <param name="personId">The Jellyfin person id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    [HttpPost("Person/{personId}/Send")]
    [Authorize(Policy = "RequiresElevation")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AcquisitionSendResult>> Send([FromRoute] Guid personId, [FromQuery] string? gapId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return NotFound();
        }

        var gap = await _missing.FindGapAsync(personId, gapId ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (gap is null)
        {
            return new AcquisitionSendResult { Success = false, Failed = 1, Message = "That title is no longer missing for this person; reload the page." };
        }

        var result = await _acquisition.SendToArrAsync(gap, Plugin.RequireConfiguration(), cancellationToken).ConfigureAwait(false);
        return new AcquisitionSendResult
        {
            Success = result.Success,
            Succeeded = result.Success ? 1 : 0,
            Failed = result.Success ? 0 : 1,
            Message = result.Message
        };
    }
}
