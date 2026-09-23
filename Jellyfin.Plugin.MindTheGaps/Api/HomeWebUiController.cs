using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// The home-screen web UI surface: the Discover row (the recommendation gaps the scan has accumulated,
/// ranked), the want-to-watch row (the caller's own list, filtered to what the library still lacks) and its
/// title search (a movie or series no page already lists, found on TMDB and added directly), plus a
/// signed-in user's todo-list add on the Discover row's cards. Every endpoint answers 404 while its surface
/// is off, so a toggle takes effect on the next page load without a restart.
/// </summary>
[ApiController]
[Route("MindTheGaps")]
public class HomeWebUiController : WebUiControllerBase
{
    private readonly HomeDiscoverService _home;
    private readonly WantedRowService _wanted;
    private readonly WatchlistSearchService _search;
    private readonly TodoStore _todo;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeWebUiController"/> class.
    /// </summary>
    /// <param name="home">Builds the home screen's discovery row.</param>
    /// <param name="wanted">Builds the home screen's want-to-watch row.</param>
    /// <param name="search">Backs the want-to-watch row's title search.</param>
    /// <param name="todo">The per-user todo-list store, for the want-to-watch row's removal.</param>
    /// <param name="access">Decides what the signed-in user may be shown.</param>
    public HomeWebUiController(HomeDiscoverService home, WantedRowService wanted, WatchlistSearchService search, TodoStore todo, WebUiAccess access)
        : base(todo, access)
    {
        _home = home;
        _wanted = wanted;
        _search = search;
        _todo = todo;
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
        var result = _home.Get(size);
        var (wantingUser, wanted) = Wanting();
        result.CanTodo = wantingUser is not null;
        WantedMarker.Mark(result.Titles, wanted);
        return result;
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

    /// <summary>
    /// Searches TMDB for a movie or series to add to the want-to-watch list directly, for a title no page
    /// already lists.
    /// </summary>
    /// <param name="kind">"Movie" or "Series".</param>
    /// <param name="q">The search text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching unowned titles, or 404 while want to watch is off or for a request that cannot
    /// keep a list.</returns>
    [HttpGet("Home/Search")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<MissingTitle>>> SearchTitles([FromQuery] string? kind, [FromQuery] string? q, CancellationToken cancellationToken)
    {
        var (userId, wanted) = Wanting();
        if (userId is not { } id)
        {
            return NotFound();
        }

        var titles = await _search.SearchAsync(kind ?? string.Empty, q ?? string.Empty, cancellationToken).ConfigureAwait(false);
        WantedMarker.Mark(titles, wanted);
        return titles.ToList();
    }

    /// <summary>
    /// Adds a search result to the caller's want-to-watch list, rehydrated server-side from TMDB by kind and
    /// id rather than trusted from the client.
    /// </summary>
    /// <param name="kind">"Movie" or "Series".</param>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries added (0 or 1), or 404 while want to watch is off.</returns>
    [HttpPost("Home/Search/Todo")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> AddSearchResultToTodo([FromQuery] string? kind, [FromQuery] int tmdbId, CancellationToken cancellationToken)
        => WantOwnedGapAsync(true, null, SearchGapId(kind, tmdbId), ct => _search.FindGapAsync(kind ?? string.Empty, tmdbId, ct), add: true, cancellationToken);

    /// <summary>
    /// Takes a search result off the caller's want-to-watch list.
    /// </summary>
    /// <param name="kind">"Movie" or "Series".</param>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entries removed, or 404 while want to watch is off.</returns>
    [HttpPost("Home/Search/Todo/Remove")]
    [Authorize]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<int>> RemoveSearchResultFromTodo([FromQuery] string? kind, [FromQuery] int tmdbId, CancellationToken cancellationToken)
        => WantOwnedGapAsync(true, null, SearchGapId(kind, tmdbId), ct => _search.FindGapAsync(kind ?? string.Empty, tmdbId, ct), add: false, cancellationToken);

    // The id a search-added entry would carry, computed the same way whether or not the title is still
    // resolvable (unlike FindGapAsync, which can return null once the library owns it), so a remove still
    // finds the entry by id even when the rehydration itself no longer would.
    private static string SearchGapId(string? kind, int tmdbId)
        => string.Equals(kind, "Series", StringComparison.OrdinalIgnoreCase)
            ? GapSourceKeys.WatchlistSearchSeries.Gap(tmdbId.ToString(CultureInfo.InvariantCulture))
            : GapSourceKeys.WatchlistSearchMovie.Gap(tmdbId.ToString(CultureInfo.InvariantCulture));
}
