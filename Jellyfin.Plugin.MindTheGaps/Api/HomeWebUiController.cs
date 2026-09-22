using System;
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
/// The home-screen web UI surface: the Discover row (the recommendation gaps the scan has accumulated,
/// ranked) and the want-to-watch row (the caller's own list, filtered to what the library still lacks),
/// plus an administrator's Send and a signed-in user's todo-list add on the Discover row's cards. Every
/// endpoint answers 404 while its surface is off, so a toggle takes effect on the next page load without a
/// restart.
/// </summary>
[ApiController]
[Route("MindTheGaps")]
public class HomeWebUiController : WebUiControllerBase
{
    private readonly HomeDiscoverService _home;
    private readonly WantedRowService _wanted;
    private readonly TodoStore _todo;
    private readonly AcquisitionService _acquisition;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeWebUiController"/> class.
    /// </summary>
    /// <param name="home">Builds the home screen's discovery row.</param>
    /// <param name="wanted">Builds the home screen's want-to-watch row.</param>
    /// <param name="acquisition">The acquisition handoff service (Radarr/Sonarr).</param>
    /// <param name="todo">The per-user todo-list store, for the want-to-watch row's removal.</param>
    /// <param name="access">Decides what the signed-in user may be shown.</param>
    public HomeWebUiController(HomeDiscoverService home, WantedRowService wanted, AcquisitionService acquisition, TodoStore todo, WebUiAccess access)
        : base(acquisition, todo, access)
    {
        _home = home;
        _wanted = wanted;
        _todo = todo;
        _acquisition = acquisition;
    }

    private static bool HomeRowEnabled => WebUiGate.HomeRow(Plugin.Instance?.Configuration);

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
        if (!HomeRowEnabled || !Access.MaySee(User))
        {
            return NotFound();
        }

        var size = limit is > 0 ? Math.Min(limit.Value, 100) : Plugin.RequireConfiguration().HomeRowSize;
        var result = _home.Get(IsAdministrator, size);
        var (wantingUser, wanted) = Wanting();
        result.CanTodo = wantingUser is not null;
        WantedMarker.Mark(result.Titles, wanted);
        return result;
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
    /// Adds one of the home row's recommendations to the caller's want-to-watch list, rehydrated server-side
    /// from the current report by its id.
    /// </summary>
    /// <param name="gapId">The gap id the row showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries added (0 or 1), or 404 while the surface is off.</returns>
    [HttpPost("Home/Todo")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> AddHomeGapToTodo([FromQuery] string? gapId, CancellationToken cancellationToken)
        => WantOwnedGapAsync(HomeRowEnabled, null, gapId, _ => Task.FromResult(_home.FindGap(gapId ?? string.Empty)), add: true, cancellationToken);

    /// <summary>
    /// Takes one of the home row's recommendations off the caller's want-to-watch list.
    /// </summary>
    /// <param name="gapId">The gap id the row showed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries removed, or 404 while the surface is off.</returns>
    [HttpPost("Home/Todo/Remove")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> RemoveHomeGapFromTodo([FromQuery] string? gapId, CancellationToken cancellationToken)
        => WantOwnedGapAsync(HomeRowEnabled, null, gapId, _ => Task.FromResult(_home.FindGap(gapId ?? string.Empty)), add: false, cancellationToken);

    /// <summary>
    /// The home screen's want-to-watch row: the movies and series on the caller's own list that are not done
    /// and that the library does not hold yet.
    /// </summary>
    /// <param name="limit">The most titles to return; omitted uses the configured row size.</param>
    /// <returns>The row, or 404 while want to watch is off or for a request that cannot keep a list.</returns>
    [HttpGet("Home/Wanted")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<WantedRowResult> GetHomeWanted([FromQuery] int? limit)
    {
        var (userId, _) = Wanting();
        if (userId is not { } id)
        {
            return NotFound();
        }

        var size = limit is > 0 ? Math.Min(limit.Value, 100) : Plugin.RequireConfiguration().HomeRowSize;
        return _wanted.Get(id, size);
    }

    /// <summary>
    /// Takes an entry off the want-to-watch row, by the id it has on the caller's own list.
    /// </summary>
    /// <param name="gapId">The entry id the row showed.</param>
    /// <returns>The number of entries removed (0 or 1), or 404 while want to watch is off or for a request that
    /// cannot keep a list.</returns>
    [HttpPost("Home/Wanted/Remove")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<int> RemoveHomeWanted([FromQuery] string? gapId)
    {
        var (userId, _) = Wanting();
        return userId is not { } id ? NotFound() : _todo.Remove(id, gapId ?? string.Empty);
    }
}
