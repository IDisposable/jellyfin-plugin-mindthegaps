using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using Jellyfin.Plugin.MindTheGaps.VirtualItems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// REST endpoints backing the Mind the Gaps dashboard.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("MindTheGaps")]
[Produces("application/json")]
public class GapsController : ControllerBase
{
    private readonly GapStore _store;
    private readonly GapScanRunner _scanRunner;
    private readonly AvailabilityService _availabilityService;
    private readonly VirtualMovieMinter _minter;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapsController"/> class.
    /// </summary>
    /// <param name="store">The gap store.</param>
    /// <param name="scanRunner">The background scan runner.</param>
    /// <param name="availabilityService">The availability service.</param>
    /// <param name="minter">The experimental virtual-movie minter.</param>
    public GapsController(GapStore store, GapScanRunner scanRunner, AvailabilityService availabilityService, VirtualMovieMinter minter)
    {
        _store = store;
        _scanRunner = scanRunner;
        _availabilityService = availabilityService;
        _minter = minter;
    }

    /// <summary>
    /// Gets the latest gap report (todo list).
    /// </summary>
    /// <returns>The latest report.</returns>
    [HttpGet("Gaps")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<GapReport> GetGaps() => _store.Load();

    /// <summary>
    /// Starts a scan in the background and returns immediately. Poll <see cref="GetScanStatus"/> for
    /// completion, then reload the report. Runs in the background so a large library cannot time out
    /// the request.
    /// </summary>
    /// <returns>The scan status, with Started indicating whether this call kicked off a new scan.</returns>
    [HttpPost("Scan")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ScanStatus> Scan()
    {
        var started = _scanRunner.TryStart();
        return new ScanStatus { Running = true, Started = started, Progress = _scanRunner.Progress };
    }

    /// <summary>
    /// Gets whether a background scan is currently running, and its progress.
    /// </summary>
    /// <returns>The scan status.</returns>
    [HttpGet("ScanStatus")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ScanStatus> GetScanStatus()
        => new ScanStatus { Running = _scanRunner.IsRunning, Progress = _scanRunner.Progress };

    /// <summary>
    /// EXPERIMENTAL. Mints pathless virtual movies into BoxSets for missing collection parts. Requires
    /// SetCompletion to be selected in the configuration's MintPatterns. Reverse with
    /// <see cref="RemoveMintedMovies"/>.
    /// </summary>
    /// <param name="dryRun">When true, logs what would be minted without writing anything. Returns the would-mint count.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of virtual movies minted (or, in a dry run, that would be minted).</returns>
    [HttpPost("MintVirtualMovies")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> MintVirtualMovies([FromQuery] bool dryRun, CancellationToken cancellationToken)
        => await _minter.MintAsync(dryRun, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Removes every virtual movie this plugin has minted (the undo for the experiment).
    /// </summary>
    /// <param name="dryRun">When true, logs what would be removed without deleting anything. Returns the would-remove count.</param>
    /// <returns>The number of virtual movies removed (or, in a dry run, that would be removed).</returns>
    [HttpPost("RemoveMintedMovies")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> RemoveMintedMovies([FromQuery] bool dryRun)
        => await _minter.RemoveAllAsync(dryRun).ConfigureAwait(false);

    /// <summary>
    /// EXPERIMENTAL debug aid. Mints a single gap (posted from a dashboard row) as a virtual movie. A
    /// collection gap goes into its BoxSet; a filmography gap goes into a catch-all collection and
    /// attaches the person so it surfaces on that person's page. Movie gaps only. Reverse with
    /// <see cref="RemoveMintedMovies"/>.
    /// </summary>
    /// <param name="dryRun">When true, logs what would happen without writing anything.</param>
    /// <param name="gap">The gap to mint.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A human-readable status message.</returns>
    [HttpPost("MintGap")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<string>> MintGap([FromQuery] bool dryRun, [FromBody] GapItem gap, CancellationToken cancellationToken)
        => await _minter.MintGapAsync(gap, dryRun, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Gets streaming availability for a single title (fetched lazily, on demand).
    /// </summary>
    /// <param name="targetKind">"Movie" or "Series".</param>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="imdbId">The IMDb id, if known.</param>
    /// <param name="country">The country code; defaults to the configured country.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The offers.</returns>
    [HttpGet("Availability")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AvailabilityOffer>>> GetAvailability(
        [FromQuery] string? targetKind,
        [FromQuery] int? tmdbId,
        [FromQuery] string? imdbId,
        [FromQuery] string? country,
        CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tmdbId.HasValue)
        {
            providerIds["Tmdb"] = tmdbId.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrEmpty(imdbId))
        {
            providerIds["Imdb"] = imdbId;
        }

        var query = new AvailabilityQuery
        {
            TargetKind = Enum.TryParse<BaseItemKind>(targetKind, true, out var kind) ? kind : BaseItemKind.Movie,
            ProviderIds = providerIds,
            Country = string.IsNullOrEmpty(country) ? config.MetadataCountryCode : country
        };

        var offers = await _availabilityService.GetOffersAsync(query, config, cancellationToken).ConfigureAwait(false);
        return Ok(offers);
    }
}
