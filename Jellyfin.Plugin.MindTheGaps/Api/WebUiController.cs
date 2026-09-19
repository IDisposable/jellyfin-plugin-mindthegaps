using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// The web UI surfaces' endpoints: the client script the web client loads; a person's unowned filmography,
/// a title's unowned similar titles, and the home screen's discovery row; and, for administrators, a Send
/// and a todo-list add on each. Every data endpoint answers 404 while its surface is off, so a toggle takes
/// effect on the next page load without a restart.
/// </summary>
[ApiController]
[Route("MindTheGaps")]
public class WebUiController : ControllerBase
{
    /// <summary>
    /// The route of the client script, relative to the server root, as the injected script tag references it.
    /// </summary>
    public const string ClientScriptPath = "MindTheGaps/WebUi/client.js";

    private const string ClientScriptResource = "Jellyfin.Plugin.MindTheGaps.Web.mindthegaps.webui.js";

    private static readonly Lazy<EmbeddedAsset?> _clientScript = new(
        () => EmbeddedAsset.Load(typeof(WebUiController).Assembly, ClientScriptResource, "application/javascript"));

    private readonly PersonMissingService _person;
    private readonly RelatedMissingService _related;
    private readonly HomeDiscoverService _home;
    private readonly AcquisitionService _acquisition;
    private readonly TodoStore _todo;
    private readonly TmdbClient _tmdb;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebUiController"/> class.
    /// </summary>
    /// <param name="person">Computes a person's unowned filmography.</param>
    /// <param name="related">Computes a title's unowned similar titles.</param>
    /// <param name="home">Builds the home screen's discovery row.</param>
    /// <param name="acquisition">The acquisition handoff service (Radarr/Sonarr).</param>
    /// <param name="todo">The personal todo-list store, for the "Add to TODO" fallback when no arr is set up.</param>
    /// <param name="tmdb">The TMDB client, for the detail dialog's title lookup.</param>
    public WebUiController(PersonMissingService person, RelatedMissingService related, HomeDiscoverService home, AcquisitionService acquisition, TodoStore todo, TmdbClient tmdb)
    {
        _person = person;
        _related = related;
        _home = home;
        _acquisition = acquisition;
        _todo = todo;
        _tmdb = tmdb;
    }

    private static bool ScriptEnabled => WebUiGate.ScriptInjected(Plugin.Instance?.Configuration);

    private static bool PersonPageEnabled => WebUiGate.PersonPage(Plugin.Instance?.Configuration);

    private static bool ItemPageEnabled => WebUiGate.ItemPage(Plugin.Instance?.Configuration);

    private static bool HomeRowEnabled => WebUiGate.HomeRow(Plugin.Instance?.Configuration);

    private bool IsAdministrator => User.IsInRole("Administrator");

    /// <summary>
    /// Serves the client script that renders the web UI surfaces. Anonymous because index.html loads it before
    /// sign-in; it carries no data and calls the authenticated endpoints below through the web client's own
    /// session.
    /// </summary>
    /// <returns>The script, or 404 while the master switch is off.</returns>
    [HttpGet("WebUi/client.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetClientScript()
    {
        if (!ScriptEnabled)
        {
            return NotFound();
        }

        var script = _clientScript.Value;
        if (script is null)
        {
            return NotFound();
        }

        // Revalidate on every load (a plugin update must not be masked by a cached copy) but answer 304 cheaply.
        // Public: it is the same script for everyone and carries nothing private, so a CDN may hold it too.
        Response.Headers[HeaderNames.CacheControl] = "public, no-cache";
        return File(script.Bytes, script.ContentType, script.LastModified, script.ETag);
    }

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
        if (!PersonPageEnabled)
        {
            return NotFound();
        }

        var result = await _person.GetAsync(personId, IsAdministrator, cancellationToken).ConfigureAwait(false);
        return result is null ? NotFound() : result;
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
    [Authorize(Policy = "RequiresElevation")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> AddPersonGapToTodo([FromRoute] Guid personId, [FromQuery] string? gapId, CancellationToken cancellationToken)
        => AddOwnedGapToTodoAsync(PersonPageEnabled, ct => _person.FindGapAsync(personId, gapId ?? string.Empty, ct), cancellationToken);

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
        if (!ItemPageEnabled)
        {
            return NotFound();
        }

        var result = await _related.GetAsync(itemId, IsAdministrator, cancellationToken).ConfigureAwait(false);
        return result is null ? NotFound() : result;
    }

    /// <summary>
    /// Sends one of an owned title's unowned similar titles to Radarr or Sonarr, rehydrated server-side from
    /// the same lookup the page listed.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="qualityProfileId">Overrides the configured default quality profile, from the dialog's
    /// picker; omitted uses the configured default.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome, or 404 while the surface is off.</returns>
    [HttpPost("Item/{itemId}/Send")]
    [Authorize(Policy = "RequiresElevation")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<AcquisitionSendResult>> SendItemGap([FromRoute] Guid itemId, [FromQuery] string? gapId, [FromQuery] int? qualityProfileId, CancellationToken cancellationToken)
        => SendOwnedGapAsync(
            ItemPageEnabled,
            ct => _related.FindGapAsync(itemId, gapId ?? string.Empty, ct),
            qualityProfileId,
            "That title is no longer listed here; refresh the page and try again.",
            cancellationToken);

    /// <summary>
    /// Adds one of an owned title's unowned similar titles to the caller's personal todo list, rehydrated
    /// server-side the same way a Send is.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="gapId">The gap id the page showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries added (0 or 1), or 404 while the surface is off.</returns>
    [HttpPost("Item/{itemId}/Todo")]
    [Authorize(Policy = "RequiresElevation")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> AddItemGapToTodo([FromRoute] Guid itemId, [FromQuery] string? gapId, CancellationToken cancellationToken)
        => AddOwnedGapToTodoAsync(ItemPageEnabled, ct => _related.FindGapAsync(itemId, gapId ?? string.Empty, ct), cancellationToken);

    /// <summary>
    /// The home screen's discovery row: the recommendation gaps the scan has accumulated, ranked.
    /// </summary>
    /// <param name="limit">The most titles to return; omitted uses the configured row size.</param>
    /// <returns>The row, or 404 while the surface is off.</returns>
    [HttpGet("Home/Discover")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<HomeDiscoverResult> GetHomeDiscover([FromQuery] int? limit)
    {
        if (!HomeRowEnabled)
        {
            return NotFound();
        }

        var size = limit is > 0 ? Math.Min(limit.Value, 100) : Plugin.RequireConfiguration().HomeRowSize;
        return _home.Get(IsAdministrator, size);
    }

    /// <summary>
    /// Sends one of the home row's recommendations to Radarr or Sonarr, rehydrated server-side from the
    /// current report by its id.
    /// </summary>
    /// <param name="gapId">The gap id the row showed.</param>
    /// <param name="qualityProfileId">Overrides the configured default quality profile, from the dialog's
    /// picker; omitted uses the configured default.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome, or 404 while the surface is off.</returns>
    [HttpPost("Home/Send")]
    [Authorize(Policy = "RequiresElevation")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AcquisitionSendResult>> SendHomeGap([FromQuery] string? gapId, [FromQuery] int? qualityProfileId, CancellationToken cancellationToken)
    {
        if (!HomeRowEnabled)
        {
            return NotFound();
        }

        var gap = _home.FindGap(gapId ?? string.Empty);
        if (gap is null)
        {
            return new AcquisitionSendResult { Success = false, Failed = 1, Message = "That title is no longer on the Discover row; refresh the page and try again." };
        }

        var config = Plugin.RequireConfiguration();
        var result = await _acquisition.SendToArrAsync(gap, config, qualityProfileId, cancellationToken).ConfigureAwait(false);
        return ToSendResult(result);
    }

    /// <summary>
    /// Adds one of the home row's recommendations to the caller's personal todo list, rehydrated
    /// server-side from the current report by its id.
    /// </summary>
    /// <param name="gapId">The gap id the row showed.</param>
    /// <returns>The number of entries added (0 or 1), or 404 while the surface is off.</returns>
    [HttpPost("Home/Todo")]
    [Authorize(Policy = "RequiresElevation")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<int> AddHomeGapToTodo([FromQuery] string? gapId)
    {
        if (!HomeRowEnabled)
        {
            return NotFound();
        }

        var gap = _home.FindGap(gapId ?? string.Empty);
        return gap is null ? 0 : _todo.Add([gap]);
    }

    /// <summary>
    /// The detail dialog's TMDB lookup for one card: enough to decide whether the title is worth acquiring
    /// before sending it anywhere. Shared by all three surfaces (person, item, home), since a TMDB id and
    /// kind is all a lookup needs; the gap itself is rehydrated separately, by the surface-specific Send.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="kind">The title's kind, <c>Movie</c> or <c>Series</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The detail, or 404 when the kind is not recognized or TMDB has nothing for that id.</returns>
    [HttpGet("WebUi/Detail")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MissingTitleDetail>> GetDetail([FromQuery] int tmdbId, [FromQuery] string? kind, CancellationToken cancellationToken)
    {
        var config = Plugin.RequireConfiguration();
        if (string.Equals(kind, "Movie", StringComparison.OrdinalIgnoreCase))
        {
            var movie = await _tmdb.GetMovieDetailsAsync(tmdbId, config.MetadataLanguage, config.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
            return movie is null ? NotFound() : MissingTitleDetailMapper.FromMovie(movie, _tmdb.GetPosterUrl, _tmdb.GetBackdropUrl);
        }

        if (string.Equals(kind, "Series", StringComparison.OrdinalIgnoreCase))
        {
            var show = await _tmdb.GetSeriesDetailsAsync(tmdbId, config.MetadataLanguage, config.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
            return show is null ? NotFound() : MissingTitleDetailMapper.FromSeries(show, _tmdb.GetPosterUrl, _tmdb.GetBackdropUrl);
        }

        return NotFound();
    }

    /// <summary>
    /// The quality profiles offered for a title's kind, for the detail dialog's picker.
    /// </summary>
    /// <param name="kind">The title's kind, <c>Movie</c> (Radarr) or <c>Series</c> (Sonarr).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The profiles, empty when the matching arr is not configured; 404 when the kind is not
    /// recognized.</returns>
    [HttpGet("WebUi/Profiles")]
    [Authorize(Policy = "RequiresElevation")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QualityProfilesResult>> GetProfiles([FromQuery] string? kind, CancellationToken cancellationToken)
    {
        var config = Plugin.RequireConfiguration();
        if (string.Equals(kind, "Movie", StringComparison.OrdinalIgnoreCase))
        {
            var profiles = await _acquisition.GetRadarrQualityProfilesAsync(config, cancellationToken).ConfigureAwait(false);
            return new QualityProfilesResult { Profiles = profiles, DefaultId = config.RadarrQualityProfileId };
        }

        if (string.Equals(kind, "Series", StringComparison.OrdinalIgnoreCase))
        {
            var profiles = await _acquisition.GetSonarrQualityProfilesAsync(config, cancellationToken).ConfigureAwait(false);
            return new QualityProfilesResult { Profiles = profiles, DefaultId = config.SonarrQualityProfileId };
        }

        return NotFound();
    }

    // The shape shared by every "send one of this owning item's gaps" endpoint (person, item; home has no
    // owning item, so stays separate): gated on the surface toggle, the gap rehydrated server-side by its
    // own lookup rather than trusted from the client, a canned per-surface message when it is gone.
    private async Task<ActionResult<AcquisitionSendResult>> SendOwnedGapAsync(
        bool surfaceEnabled,
        Func<CancellationToken, Task<GapItem?>> findGap,
        int? qualityProfileId,
        string notFoundMessage,
        CancellationToken cancellationToken)
    {
        if (!surfaceEnabled)
        {
            return NotFound();
        }

        var gap = await findGap(cancellationToken).ConfigureAwait(false);
        if (gap is null)
        {
            return new AcquisitionSendResult { Success = false, Failed = 1, Message = notFoundMessage };
        }

        var config = Plugin.RequireConfiguration();
        var result = await _acquisition.SendToArrAsync(gap, config, qualityProfileId, cancellationToken).ConfigureAwait(false);
        return ToSendResult(result);
    }

    // The todo-list sibling of SendOwnedGapAsync: same gate and rehydration, but TodoStore.Add takes any
    // GapItem regardless of provenance, so there is no per-surface failure message to thread through, just
    // a plain added count (0 or 1) matching Api/TodoController's own AddTodo contract.
    private async Task<ActionResult<int>> AddOwnedGapToTodoAsync(bool surfaceEnabled, Func<CancellationToken, Task<GapItem?>> findGap, CancellationToken cancellationToken)
    {
        if (!surfaceEnabled)
        {
            return NotFound();
        }

        var gap = await findGap(cancellationToken).ConfigureAwait(false);
        return gap is null ? 0 : _todo.Add([gap]);
    }

    private static AcquisitionSendResult ToSendResult(AcquisitionResult result)
        => new()
        {
            Success = result.Success,
            Succeeded = result.Success ? 1 : 0,
            Failed = result.Success ? 0 : 1,
            Message = result.Message
        };
}
