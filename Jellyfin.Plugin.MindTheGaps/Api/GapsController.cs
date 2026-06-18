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
    private readonly AvailabilityRunner _availabilityRunner;
    private readonly VirtualMovieMinter _minter;
    private readonly MintRunner _mintRunner;
    private readonly ResolutionStore _resolutions;
    private readonly ScanCursorStore _cursors;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapsController"/> class.
    /// </summary>
    /// <param name="store">The gap store.</param>
    /// <param name="scanRunner">The background scan runner.</param>
    /// <param name="availabilityService">The availability service.</param>
    /// <param name="availabilityRunner">The background availability enrichment runner.</param>
    /// <param name="minter">The experimental virtual-movie minter.</param>
    /// <param name="mintRunner">The background mint runner.</param>
    /// <param name="resolutions">The store of per-gap resolution notes.</param>
    /// <param name="cursors">The scan-rotation cursor store.</param>
    public GapsController(GapStore store, GapScanRunner scanRunner, AvailabilityService availabilityService, AvailabilityRunner availabilityRunner, VirtualMovieMinter minter, MintRunner mintRunner, ResolutionStore resolutions, ScanCursorStore cursors)
    {
        _store = store;
        _scanRunner = scanRunner;
        _availabilityService = availabilityService;
        _availabilityRunner = availabilityRunner;
        _minter = minter;
        _mintRunner = mintRunner;
        _resolutions = resolutions;
        _cursors = cursors;
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
    /// Clears the scan-rotation state so the capped sources (filmography, recommendations) treat every
    /// item as never-scanned and start a fresh coverage cycle on the next scan. Does not delete any gaps
    /// or dismissals.
    /// </summary>
    /// <returns>No content.</returns>
    [HttpPost("ResetScanRotation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult ResetScanRotation()
    {
        _cursors.Reset();
        return NoContent();
    }

    /// <summary>
    /// Starts a background removal of every virtual movie this plugin has minted. Poll <see cref="GetMintStatus"/>.
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
            return string.Create(CultureInfo.InvariantCulture, $"{(dryRun ? "Would remove" : "Removed")} {n} minted virtual movies.");
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
    /// EXPERIMENTAL. Starts a background mint of several gaps (the report's multi-select). Each goes
    /// through the same one-off path as <see cref="MintGap"/>. Runs in the background so a large
    /// selection cannot time out the request; poll <see cref="GetMintStatus"/>.
    /// </summary>
    /// <param name="dryRun">When true, logs what would happen without writing anything.</param>
    /// <param name="gaps">The gaps to mint.</param>
    /// <returns>The mint status.</returns>
    [HttpPost("MintGaps")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<MintStatus> MintGaps([FromQuery] bool dryRun, [FromBody] IReadOnlyList<GapItem> gaps)
    {
        var started = _mintRunner.TryStart((progress, ct) => _minter.MintGapsAsync(gaps, dryRun, progress, ct));
        return new MintStatus { Running = true, Started = started };
    }

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

    /// <summary>
    /// Starts a background pass that looks up "where to watch" for the watchable gaps in the current
    /// report that do not have it yet, so the "Hide items with no sources" filter gets data without a
    /// rescan. Returns immediately; poll <see cref="GetAvailabilityStatus"/>.
    /// </summary>
    /// <returns>The enrichment status, with Started indicating whether this call kicked off a new pass.</returns>
    [HttpPost("Availability/Enrich")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<AvailabilityStatus> EnrichAvailability()
    {
        var started = _availabilityRunner.TryStart();
        return new AvailabilityStatus { Running = true, Started = started, Progress = _availabilityRunner.Progress };
    }

    /// <summary>
    /// Gets whether the background availability pass is running, its progress, and the last completed message.
    /// </summary>
    /// <returns>The enrichment status.</returns>
    [HttpGet("Availability/Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<AvailabilityStatus> GetAvailabilityStatus()
        => new AvailabilityStatus { Running = _availabilityRunner.IsRunning, Progress = _availabilityRunner.Progress, Message = _availabilityRunner.LastMessage ?? string.Empty };

    /// <summary>
    /// Gets every gap resolution (gaps marked as not really missing), keyed by gap id.
    /// </summary>
    /// <returns>The resolutions.</returns>
    [HttpGet("Resolutions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyDictionary<string, GapResolution>> GetResolutions() => Ok(_resolutions.GetAll());

    /// <summary>
    /// Marks a gap resolved (not really missing) with a note. Persists across rescans.
    /// </summary>
    /// <param name="request">The gap id and note.</param>
    /// <returns>No content.</returns>
    [HttpPost("Resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Resolve([FromBody] ResolveRequest request)
    {
        _resolutions.SetState(request.Id, request.Kind, request.Note, request.SnoozedUntil);
        return NoContent();
    }

    /// <summary>
    /// Clears a gap's resolution so it shows as missing again.
    /// </summary>
    /// <param name="id">The gap id.</param>
    /// <returns>No content.</returns>
    [HttpPost("Unresolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Unresolve([FromQuery] string id)
    {
        _resolutions.Clear(id);
        return NoContent();
    }
}
