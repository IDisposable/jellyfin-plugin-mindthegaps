using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// Endpoints that hand a gap off to an acquisition target (Radarr, Sonarr, or Jellyseerr/Overseerr). Each gap
/// is rehydrated server-side from the stored report by its id, never trusted from the client. Shares the
/// <c>MindTheGaps</c> route.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("MindTheGaps")]
[Produces("application/json")]
public class AcquisitionController : ControllerBase
{
    private readonly GapStore _store;
    private readonly AcquisitionService _acquisition;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcquisitionController"/> class.
    /// </summary>
    /// <param name="store">The gap store, to rehydrate a gap by id.</param>
    /// <param name="acquisition">The acquisition handoff service (Radarr/Sonarr/Jellyseerr).</param>
    public AcquisitionController(GapStore store, AcquisitionService acquisition)
    {
        _store = store;
        _acquisition = acquisition;
    }

    /// <summary>
    /// Tells the dashboard which acquisition targets are configured, so a Send button appears on a row only
    /// for a target that is set up. Keys and URLs stay on the server.
    /// </summary>
    /// <returns>The per-target configured flags.</returns>
    [HttpGet("AcquisitionConfig")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<AcquisitionConfigStatus> GetAcquisitionConfig()
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return new AcquisitionConfigStatus
        {
            RadarrConfigured = AcquisitionService.RadarrConfigured(config),
            SonarrConfigured = AcquisitionService.SonarrConfigured(config),
            SeerrConfigured = AcquisitionService.SeerrConfigured(config)
        };
    }

    /// <summary>
    /// Sends one gap to Radarr (a movie) or Sonarr (a series/episode), rehydrated server-side by its id.
    /// </summary>
    /// <param name="id">The stable id of the gap to send.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    [HttpPost("SendToArr")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AcquisitionSendResult>> SendToArr([FromQuery] string? id, CancellationToken cancellationToken)
        => await SendOneAsync(id, _acquisition.SendToArrAsync, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Sends several selected gaps to Radarr/Sonarr, rehydrated server-side by their ids. One failed send
    /// does not stop the rest.
    /// </summary>
    /// <param name="ids">The stable ids of the gaps to send.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The aggregate outcome.</returns>
    [HttpPost("SendToArrBulk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AcquisitionSendResult>> SendToArrBulk([FromBody] IReadOnlyList<string> ids, CancellationToken cancellationToken)
        => await SendManyAsync(ids, _acquisition.SendToArrAsync, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Requests one gap in Jellyseerr/Overseerr, rehydrated server-side by its id.
    /// </summary>
    /// <param name="id">The stable id of the gap to request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    [HttpPost("SendToSeerr")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AcquisitionSendResult>> SendToSeerr([FromQuery] string? id, CancellationToken cancellationToken)
        => await SendOneAsync(id, _acquisition.SendToSeerrAsync, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Requests several selected gaps in Jellyseerr/Overseerr, rehydrated server-side by their ids.
    /// </summary>
    /// <param name="ids">The stable ids of the gaps to request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The aggregate outcome.</returns>
    [HttpPost("SendToSeerrBulk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AcquisitionSendResult>> SendToSeerrBulk([FromBody] IReadOnlyList<string> ids, CancellationToken cancellationToken)
        => await SendManyAsync(ids, _acquisition.SendToSeerrAsync, cancellationToken).ConfigureAwait(false);

    // Run one send for a gap rehydrated from the stored report (never trust a client-posted gap object).
    private async Task<AcquisitionSendResult> SendOneAsync(string? id, Func<GapItem, PluginConfiguration, CancellationToken, Task<AcquisitionResult>> send, CancellationToken cancellationToken)
    {
        var gap = _store.FindById(id);
        if (gap is null)
        {
            return new AcquisitionSendResult { Success = false, Failed = 1, Message = "That gap is no longer in the current report; rescan and try again." };
        }

        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return new AcquisitionSendResult { Success = false, Failed = 1, Message = "The plugin has not been set up yet." };
        }

        var result = await send(gap, config, cancellationToken).ConfigureAwait(false);
        return new AcquisitionSendResult
        {
            Success = result.Success,
            Succeeded = result.Success ? 1 : 0,
            Failed = result.Success ? 0 : 1,
            Message = result.Message
        };
    }

    // Run a send for each rehydrated gap, continuing past failures and aggregating the outcome.
    private async Task<AcquisitionSendResult> SendManyAsync(IReadOnlyList<string> ids, Func<GapItem, PluginConfiguration, CancellationToken, Task<AcquisitionResult>> send, CancellationToken cancellationToken)
    {
        var wanted = new HashSet<string>(ids ?? [], StringComparer.Ordinal);
        var gaps = _store.LoadSnapshot().Items.Where(i => wanted.Contains(i.Id)).ToArray();
        if (gaps.Length == 0)
        {
            return new AcquisitionSendResult { Success = false, Message = "None of those gaps are in the current report; rescan and try again." };
        }

        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return new AcquisitionSendResult { Success = false, Failed = gaps.Length, Message = "The plugin has not been set up yet." };
        }

        var succeeded = 0;
        var failed = 0;
        string? firstFailure = null;
        foreach (var gap in gaps)
        {
            var result = await send(gap, config, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                succeeded++;
            }
            else
            {
                failed++;
                firstFailure ??= result.Message;
            }
        }

        var message = failed == 0
            ? string.Create(CultureInfo.InvariantCulture, $"Sent {succeeded} item(s).")
            : string.Create(CultureInfo.InvariantCulture, $"Sent {succeeded}, {failed} failed. First failure: {firstFailure}");
        return new AcquisitionSendResult { Success = failed == 0, Succeeded = succeeded, Failed = failed, Message = message };
    }
}
