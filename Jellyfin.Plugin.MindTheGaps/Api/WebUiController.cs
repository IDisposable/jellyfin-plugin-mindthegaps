using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// The web UI surfaces' cross-surface endpoints: the client script every surface's page loads, and the
/// detail dialog's TMDB lookup, shared by the person/item/home surfaces (<see cref="PersonWebUiController"/>,
/// <see cref="ItemWebUiController"/>, <see cref="HomeWebUiController"/>) since a TMDB id and kind is all
/// any of them needs. Deliberately carries no acquisition handoff: an administrator monitors what everyone
/// wants through the report's own Maintenance section (the fulfillment queue) instead, so this surface
/// never talks to Radarr or Sonarr.
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

    private readonly TmdbClient _tmdb;
    private readonly JustWatchLinkIndex _justWatchLinks;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebUiController"/> class.
    /// </summary>
    /// <param name="tmdb">The TMDB client, for the detail dialog's title lookup.</param>
    /// <param name="justWatchLinks">Finds a title's own JustWatch page among the report's links.</param>
    public WebUiController(TmdbClient tmdb, JustWatchLinkIndex justWatchLinks)
    {
        _tmdb = tmdb;
        _justWatchLinks = justWatchLinks;
    }

    private static bool ScriptEnabled => WebUiGate.ScriptInjected(Plugin.Instance?.Configuration);

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
    /// The detail dialog's TMDB lookup for one card. Shared by all three surfaces (person, item, home),
    /// since a TMDB id and kind is all a lookup needs.
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
            return movie is null ? NotFound() : MissingTitleDetailMapper.FromMovie(movie, _tmdb.GetPosterUrl, _tmdb.GetBackdropUrl, config.MetadataCountryCode, _justWatchLinks.Find(BaseItemKind.Movie, tmdbId));
        }

        if (string.Equals(kind, "Series", StringComparison.OrdinalIgnoreCase))
        {
            var show = await _tmdb.GetSeriesDetailsAsync(tmdbId, config.MetadataLanguage, config.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
            return show is null ? NotFound() : MissingTitleDetailMapper.FromSeries(show, _tmdb.GetPosterUrl, _tmdb.GetBackdropUrl, config.MetadataCountryCode, _justWatchLinks.Find(BaseItemKind.Series, tmdbId));
        }

        return NotFound();
    }
}
