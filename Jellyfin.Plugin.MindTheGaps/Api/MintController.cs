using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.VirtualItems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// Endpoints for minting and removing the experimental virtual placeholder items (ADR-0004). Shares the
/// <c>MindTheGaps</c> route with the other dashboard controllers.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("MindTheGaps")]
[Produces("application/json")]
public class MintController : ControllerBase
{
    private readonly GapStore _store;
    private readonly VirtualItemMinter _minter;
    private readonly MintRunner _mintRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="MintController"/> class.
    /// </summary>
    /// <param name="store">The gap store, to rehydrate a gap by id.</param>
    /// <param name="minter">The virtual-item minter.</param>
    /// <param name="mintRunner">The background mint runner.</param>
    public MintController(GapStore store, VirtualItemMinter minter, MintRunner mintRunner)
    {
        _store = store;
        _minter = minter;
        _mintRunner = mintRunner;
    }

    /// <summary>
    /// Starts a background removal of every virtual item this plugin has minted. Poll <see cref="GetMintStatus"/>.
    /// </summary>
    /// <param name="dryRun">When true, logs what would be removed without deleting anything.</param>
    /// <returns>The mint status.</returns>
    [HttpPost("RemoveMintedMovies")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<MintStatus> RemoveMintedMovies([FromQuery] bool dryRun)
    {
        var started = _mintRunner.TryStart(async (_, _) =>
        {
            var n = await _minter.RemoveAllAsync(dryRun).ConfigureAwait(false);
            return string.Create(CultureInfo.InvariantCulture, $"{(dryRun ? "Would remove" : "Removed")} {n} minted virtual items.");
        });
        return new MintStatus { Running = true, Started = started };
    }

    /// <summary>
    /// Gets whether a background mint operation is running, its progress, and the last completed message.
    /// </summary>
    /// <returns>The mint status.</returns>
    [HttpGet("MintStatus")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<MintStatus> GetMintStatus()
        => new MintStatus { Running = _mintRunner.IsRunning, Progress = _mintRunner.Progress, Message = _mintRunner.LastMessage };

    /// <summary>
    /// Temporary debug aid. Mints a single gap (posted from a dashboard row) as the right virtual item for
    /// its kind (a Movie, Series, MusicAlbum, or Book). A collection gap goes into its BoxSet; a music-album
    /// gap whose owning artist resolves goes under that artist; any other gap goes into a catch-all
    /// collection, and a filmography gap attaches the person so it surfaces on that person's page. Movie,
    /// Series, MusicAlbum, and Book gaps are mintable. Reverse with <see cref="RemoveMintedMovies"/>.
    /// </summary>
    /// <param name="dryRun">When true, logs what would happen without writing anything.</param>
    /// <param name="id">The stable id of the gap to mint (rehydrated from the stored report).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A human-readable status message.</returns>
    [HttpPost("MintGap")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<string>> MintGap([FromQuery] bool dryRun, [FromQuery] string? id, CancellationToken cancellationToken)
    {
        var gap = _store.FindById(id);
        if (gap is null)
        {
            return "That gap is no longer in the current report; rescan and try again.";
        }

        return await _minter.MintGapAsync(gap, dryRun, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Temporary. Starts a background mint of several gaps (the report's multi-select). Each goes
    /// through the same one-off path as <see cref="MintGap"/>. Runs in the background so a large
    /// selection cannot time out the request; poll <see cref="GetMintStatus"/>.
    /// </summary>
    /// <param name="dryRun">When true, logs what would happen without writing anything.</param>
    /// <param name="ids">The stable ids of the gaps to mint (rehydrated from the stored report).</param>
    /// <returns>The mint status.</returns>
    [HttpPost("MintGaps")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<MintStatus> MintGaps([FromQuery] bool dryRun, [FromBody] IReadOnlyList<string> ids)
    {
        // Rehydrate from the server's own report rather than trusting client-sent gap objects, so a stale
        // or tampered payload cannot drive a mint with arbitrary fields. Unknown ids are dropped.
        var wanted = new HashSet<string>(ids ?? Array.Empty<string>(), StringComparer.Ordinal);
        var gaps = _store.LoadSnapshot().Items.Where(i => wanted.Contains(i.Id)).ToArray();
        if (gaps.Length == 0)
        {
            return new MintStatus { Running = false, Started = false, Message = "None of those gaps are in the current report; rescan and try again." };
        }

        var started = _mintRunner.TryStart((progress, ct) => _minter.MintGapsAsync(gaps, dryRun, progress, ct));
        return new MintStatus { Running = true, Started = started };
    }
}
