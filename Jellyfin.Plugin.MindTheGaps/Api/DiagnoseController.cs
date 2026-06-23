using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// Endpoints for diagnosing why a gap is reported missing and auditing the library's identification, for the
/// dashboard's Diagnose action and downloadable audit. Shares the <c>MindTheGaps</c> route.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("MindTheGaps")]
[Produces("application/json")]
public class DiagnoseController : ControllerBase
{
    private readonly GapStore _store;
    private readonly GapDiagnostics _diagnostics;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnoseController"/> class.
    /// </summary>
    /// <param name="store">The gap store, to rehydrate a gap by id and snapshot the report for the audit.</param>
    /// <param name="diagnostics">The gap identification diagnostics.</param>
    public DiagnoseController(GapStore store, GapDiagnostics diagnostics)
    {
        _store = store;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Diagnoses why a gap is reported missing: most often a metadata mismatch where the library already
    /// holds the title under a different (or absent) provider id. Library-only, so it returns immediately.
    /// </summary>
    /// <param name="id">The stable id of the gap to diagnose (rehydrated from the stored report).</param>
    /// <param name="deeper">When true, confirm against TheMovieDb (one networked pass); otherwise library-only.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The diagnosis.</returns>
    [HttpGet("Diagnose")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<GapDiagnosis>> Diagnose([FromQuery] string? id, [FromQuery] bool deeper, CancellationToken cancellationToken)
    {
        var gap = _store.FindById(id);
        if (gap is null)
        {
            return new GapDiagnosis { Summary = "That gap is no longer in the current report; rescan and try again." };
        }

        return await _diagnostics.DiagnoseAsync(gap, deeper, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Audits the whole library for identification problems (gaps you likely own under a different id, and
    /// owned items sharing a provider id), for the report's downloadable Markdown audit. Library-only.
    /// </summary>
    /// <returns>The audit.</returns>
    [HttpGet("DiagnoseAudit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IdentificationAudit> DiagnoseAudit()
    {
        return _diagnostics.BuildAudit(_store.LoadSnapshot());
    }
}
