using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// The web UI surfaces' endpoints: the client script the web client loads; the lists each surface renders (a
/// person's unowned filmography, a title's unowned similar titles, the home screen's discovery row); and,
/// shared by all of them, a detail view and a Send for administrators. Every endpoint answers 404 while its
/// surface is off, so a toggle takes effect on the next page load without a restart.
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
    private const string AdministratorRole = "Administrator";

    // The script's ETag is its content hash, not the plugin version: two builds of the same version (a dev
    // loop, a hotfix) must not leave a browser holding the older script on a 304.
    private static readonly Lazy<(byte[] Bytes, EntityTagHeaderValue ETag)?> _clientScript = new(LoadClientScript);

    private readonly PersonMissingService _person;
    private readonly RelatedMissingService _related;
    private readonly HomeDiscoverService _home;
    private readonly WantToWatchService _want;
    private readonly WatchlistSearchService _search;
    private readonly WebUiGapResolver _resolver;
    private readonly AcquisitionService _acquisition;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebUiController"/> class.
    /// </summary>
    /// <param name="person">Computes a person's unowned filmography.</param>
    /// <param name="related">Computes a title's unowned similar titles.</param>
    /// <param name="home">Builds the home screen's discovery row.</param>
    /// <param name="want">The want-to-watch list.</param>
    /// <param name="search">The watchlist search.</param>
    /// <param name="resolver">Rehydrates and describes a card's gap.</param>
    /// <param name="acquisition">The acquisition handoff service (Radarr/Sonarr).</param>
    public WebUiController(PersonMissingService person, RelatedMissingService related, HomeDiscoverService home, WantToWatchService want, WatchlistSearchService search, WebUiGapResolver resolver, AcquisitionService acquisition)
    {
        _person = person;
        _related = related;
        _home = home;
        _want = want;
        _search = search;
        _resolver = resolver;
        _acquisition = acquisition;
    }

    private static bool AnyEnabled => Plugin.Instance?.Configuration.WebUiEnabled == true;

    private bool IsAdministrator => User.IsInRole(AdministratorRole);

    private Guid CurrentUserId
    {
        get
        {
            var claim = User.FindFirst("Jellyfin-UserId")?.Value;
            return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
        }
    }

    /// <summary>
    /// Serves the client script that renders the web UI surfaces. Anonymous because index.html loads it before
    /// sign-in; it carries no data and calls the authenticated endpoints below through the web client's own
    /// session. It also tells the client which surfaces are on.
    /// </summary>
    /// <returns>The script, or 404 while every surface is off.</returns>
    [HttpGet("WebUi/client.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetClientScript()
    {
        if (!AnyEnabled)
        {
            return NotFound();
        }

        var script = _clientScript.Value;
        if (script is null)
        {
            return NotFound();
        }

        // Revalidate on every load (a plugin update must not be masked by a cached copy) but answer 304 cheaply.
        Response.Headers[HeaderNames.CacheControl] = "no-cache";
        return File(script.Value.Bytes, "application/javascript", lastModified: null, entityTag: script.Value.ETag);
    }

    /// <summary>
    /// Tells the client which surfaces are on, so it renders only those.
    /// </summary>
    /// <returns>The flags.</returns>
    [HttpGet("WebUi/Surfaces")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<WebUiSurfaces> GetSurfaces()
    {
        var c = Plugin.RequireConfiguration();
        return new WebUiSurfaces
        {
            PersonPage = c.PersonPageEnabled,
            ItemPage = c.ItemPageEnabled,
            HomeRow = c.HomeRowEnabled,
            WantToWatch = c.WantToWatchEnabled,
            CanEditWantToWatch = c.WantToWatchEnabled && IsAdministrator,
            WatchlistName = WatchlistPlaylistService.Name
        };
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
        if (!Enabled(GapSource.Person))
        {
            return NotFound();
        }

        var result = await _person.GetAsync(personId, IsAdministrator, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        MarkWanted(result.Movies);
        MarkWanted(result.Series);
        await MarkArrAsync(result.Movies, cancellationToken).ConfigureAwait(false);
        await MarkArrAsync(result.Series, cancellationToken).ConfigureAwait(false);
        return result;
    }

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
        if (!Enabled(GapSource.Item))
        {
            return NotFound();
        }

        var result = await _related.GetAsync(itemId, IsAdministrator, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        MarkWanted(result.Titles);
        await MarkArrAsync(result.Titles, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// The home screen's discovery row: the recommendation gaps the scan has accumulated, ranked.
    /// </summary>
    /// <param name="limit">The most titles to return; omitted uses the configured row size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The row, or 404 while the surface is off.</returns>
    [HttpGet("Home/Discover")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HomeDiscoverResult>> GetHomeDiscover([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (!Enabled(GapSource.Home))
        {
            return NotFound();
        }

        var size = limit is > 0 ? Math.Min(limit.Value, 100) : Plugin.RequireConfiguration().HomeRowSize;
        var result = _home.Get(IsAdministrator, size);
        MarkWanted(result.Titles);
        await MarkArrAsync(result.Titles, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// The home screen's want-to-watch row: the todo list's undone movies and series, newest added first.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The row, or 404 while the list is off.</returns>
    [HttpGet("Home/WantToWatch")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HomeDiscoverResult>> GetWantToWatch(CancellationToken cancellationToken)
    {
        if (!Enabled(GapSource.Todo))
        {
            return NotFound();
        }

        var result = await _want.GetAsync(CurrentUserId, IsAdministrator).ConfigureAwait(false);
        await MarkArrAsync(result.Titles, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Searches TMDB by title for the watchlist: owned matches carry their library item, missing ones can be
    /// bookmarked or sent.
    /// </summary>
    /// <param name="q">The title, or part of it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matches, or 404 while the list is off.</returns>
    [HttpGet("WebUi/Search")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<MissingTitle>>> Search([FromQuery] string? q, CancellationToken cancellationToken)
    {
        if (!Enabled(GapSource.Todo))
        {
            return NotFound();
        }

        var titles = await _search.SearchAsync(q ?? string.Empty, cancellationToken).ConfigureAwait(false);
        MarkWanted(titles);
        await MarkArrAsync(titles, cancellationToken).ConfigureAwait(false);
        return new ActionResult<IReadOnlyList<MissingTitle>>(titles);
    }

    /// <summary>
    /// Adds a card's title to the want-to-watch list, or removes it. The gap is rehydrated server-side from
    /// the surface it came from.
    /// </summary>
    /// <param name="source">The surface the card is on: person, item, home, or todo.</param>
    /// <param name="sourceId">The person or item id the surface is for; omitted otherwise.</param>
    /// <param name="gapId">The gap id the card showed.</param>
    /// <param name="wanted">True to add, false to remove.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the title is now on the list.</returns>
    [HttpPost("WebUi/WantToWatch")]
    [Authorize(Policy = "RequiresElevation")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WantToWatchState>> SetWantToWatch([FromQuery] string? source, [FromQuery] Guid? sourceId, [FromQuery] string? gapId, [FromQuery] bool wanted, CancellationToken cancellationToken)
    {
        if (!Enabled(GapSource.Todo) || !WebUiGapResolver.TryParse(source, out var parsed) || !Enabled(parsed))
        {
            return NotFound();
        }

        var gap = await _resolver.ResolveAsync(parsed, sourceId ?? Guid.Empty, gapId ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (!wanted)
        {
            // Remove every entry for the title, whichever surface added it; a stale id still removes itself.
            if (gap is null)
            {
                _want.Remove(gapId ?? string.Empty);
            }
            else
            {
                _want.RemoveMatching(gap);
            }

            return new WantToWatchState { GapId = gapId ?? string.Empty, Wanted = false };
        }

        if (gap is null)
        {
            return NotFound();
        }

        _want.Add(gap);
        return new WantToWatchState { GapId = gap.Id, Wanted = true };
    }

    /// <summary>
    /// Describes one card's title (overview, rating, runtime, genres, links) so the viewer can decide before
    /// sending it.
    /// </summary>
    /// <param name="source">The surface the card is on: person, item, or home.</param>
    /// <param name="sourceId">The person or item id the surface is for; omitted for the home row.</param>
    /// <param name="gapId">The gap id the card showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The detail, or 404 when the surface does not show that title or is off.</returns>
    [HttpGet("WebUi/Detail")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MissingTitleDetail>> GetDetail([FromQuery] string? source, [FromQuery] Guid? sourceId, [FromQuery] string? gapId, CancellationToken cancellationToken)
    {
        if (!WebUiGapResolver.TryParse(source, out var parsed) || !Enabled(parsed))
        {
            return NotFound();
        }

        var detail = await _resolver.DescribeAsync(parsed, sourceId ?? Guid.Empty, gapId ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return detail is null ? NotFound() : detail;
    }

    /// <summary>
    /// Lists the quality profiles Radarr and Sonarr offer, with the configured defaults, so an administrator
    /// can pick one per send.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The profiles per target.</returns>
    [HttpGet("WebUi/Profiles")]
    [Authorize(Policy = "RequiresElevation")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AcquisitionProfiles>> GetProfiles(CancellationToken cancellationToken)
    {
        if (!AnyEnabled)
        {
            return NotFound();
        }

        return await _acquisition.GetQualityProfilesAsync(Plugin.RequireConfiguration(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends one card's title to Radarr (a movie) or Sonarr (a series). The gap is rehydrated server-side from
    /// the surface it came from, never trusted from the client.
    /// </summary>
    /// <param name="source">The surface the card is on: person, item, or home.</param>
    /// <param name="sourceId">The person or item id the surface is for; omitted for the home row.</param>
    /// <param name="gapId">The gap id the card showed.</param>
    /// <param name="qualityProfileId">A quality profile chosen for this send; omitted keeps the configured default.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    [HttpPost("WebUi/Send")]
    [Authorize(Policy = "RequiresElevation")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AcquisitionSendResult>> Send([FromQuery] string? source, [FromQuery] Guid? sourceId, [FromQuery] string? gapId, [FromQuery] int? qualityProfileId, CancellationToken cancellationToken)
    {
        if (!WebUiGapResolver.TryParse(source, out var parsed) || !Enabled(parsed))
        {
            return NotFound();
        }

        var gap = await _resolver.ResolveAsync(parsed, sourceId ?? Guid.Empty, gapId ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (gap is null)
        {
            return new AcquisitionSendResult { Success = false, Failed = 1, Message = "That title is no longer listed here; reload the page." };
        }

        var result = await _acquisition.SendToArrAsync(gap, Plugin.RequireConfiguration(), cancellationToken, qualityProfileId).ConfigureAwait(false);
        _acquisition.ForgetPresence();
        return new AcquisitionSendResult
        {
            Success = result.Success,
            Succeeded = result.Success ? 1 : 0,
            Failed = result.Success ? 0 : 1,
            Message = result.Message
        };
    }

    private static bool Enabled(GapSource source) => Plugin.Instance?.Configuration is { } c && source switch
    {
        GapSource.Person => c.PersonPageEnabled,
        GapSource.Item => c.ItemPageEnabled,
        GapSource.Home => c.HomeRowEnabled,
        GapSource.Todo => c.WantToWatchEnabled,
        GapSource.Search => c.WantToWatchEnabled,
        _ => false
    };

    // Marks cards whose title Radarr/Sonarr already holds, so the client offers "in Radarr" instead of a
    // send that would be rejected as a duplicate.
    private async Task MarkArrAsync(IReadOnlyList<MissingTitle> titles, CancellationToken cancellationToken)
    {
        if (titles.Count == 0)
        {
            return;
        }

        var presence = await _acquisition.GetPresenceAsync(Plugin.RequireConfiguration(), cancellationToken).ConfigureAwait(false);
        foreach (var title in titles)
        {
            var table = title.Kind == "Movie" ? presence.Movies : presence.Series;
            if (table.TryGetValue(title.TmdbId, out var state))
            {
                title.InArr = title.Kind == "Movie" ? "Radarr" : "Sonarr";
                title.ArrState = state.HasFile ? "downloaded" : state.Monitored ? "monitored" : "unmonitored";
            }
        }
    }

    private void MarkWanted(IReadOnlyList<MissingTitle> titles)
    {
        if (Enabled(GapSource.Todo))
        {
            _want.Mark(titles);
        }
    }

    private static (byte[] Bytes, EntityTagHeaderValue ETag)? LoadClientScript()
    {
        using var stream = typeof(WebUiController).Assembly.GetManifestResourceStream(ClientScriptResource);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 16));
        return (bytes, new EntityTagHeaderValue(string.Create(CultureInfo.InvariantCulture, $"\"mtg-webui-{hash}\"")));
    }
}
