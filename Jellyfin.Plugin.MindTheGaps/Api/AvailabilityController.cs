using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// Endpoints for streaming availability ("where to watch"): a lazy per-title lookup and the background pass
/// that enriches the report. Shares the <c>MindTheGaps</c> route.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("MindTheGaps")]
[Produces("application/json")]
public class AvailabilityController : ControllerBase
{
    private readonly AvailabilityService _availabilityService;
    private readonly AvailabilityRunner _availabilityRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvailabilityController"/> class.
    /// </summary>
    /// <param name="availabilityService">The availability service.</param>
    /// <param name="availabilityRunner">The background availability enrichment runner.</param>
    public AvailabilityController(AvailabilityService availabilityService, AvailabilityRunner availabilityRunner)
    {
        _availabilityService = availabilityService;
        _availabilityRunner = availabilityRunner;
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
        => new AvailabilityStatus
        {
            Running = _availabilityRunner.IsRunning,
            Progress = _availabilityRunner.Progress,
            Processed = _availabilityRunner.Processed,
            Total = _availabilityRunner.Total,
            Message = _availabilityRunner.LastMessage ?? string.Empty
        };
}
