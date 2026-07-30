using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// REST endpoints backing the Mind the Gaps dashboard: the report, the scan, and the ad-hoc explore with its
/// curated chip type-ahead. The mint, acquisition, diagnose, availability, resolutions, and todo endpoints
/// live in their own controllers sharing this <c>MindTheGaps</c> route.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("MindTheGaps")]
[Produces("application/json")]
public class GapsController : ControllerBase
{
    private readonly GapStore _store;
    private readonly GapScanRunner _scanRunner;
    private readonly ExploreRunner _exploreRunner;
    private readonly ScanCursorStore _cursors;
    private readonly ExploreRegistry _explore;
    private readonly LibraryVerifier _verifier;
    private readonly RecheckRunner _recheckRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapsController"/> class.
    /// </summary>
    /// <param name="store">The gap store.</param>
    /// <param name="scanRunner">The background scan runner.</param>
    /// <param name="exploreRunner">The background ad-hoc explore runner.</param>
    /// <param name="cursors">The scan-rotation cursor store.</param>
    /// <param name="explore">The explore-kind registry, backing the curated type-ahead, id resolution, and kinds list.</param>
    /// <param name="verifier">The library verifier, for clearing gaps the library has since been given.</param>
    /// <param name="recheckRunner">The background bulk re-check runner.</param>
    public GapsController(GapStore store, GapScanRunner scanRunner, ExploreRunner exploreRunner, ScanCursorStore cursors, ExploreRegistry explore, LibraryVerifier verifier, RecheckRunner recheckRunner)
    {
        _store = store;
        _scanRunner = scanRunner;
        _exploreRunner = exploreRunner;
        _cursors = cursors;
        _explore = explore;
        _verifier = verifier;
        _recheckRunner = recheckRunner;
    }

    /// <summary>
    /// Gets the latest gap report (todo list), optionally narrowed to a single pattern so the dashboard
    /// can load one tab at a time instead of shipping the whole report.
    /// </summary>
    /// <param name="pattern">An optional pattern name (for example SetCompletion); omitted returns all.</param>
    /// <returns>The latest report, filtered to the pattern when one is given.</returns>
    [HttpGet("Gaps")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<GapReport> GetGaps([FromQuery] string? pattern)
    {
        var report = _store.LoadSnapshot();
        if (string.IsNullOrEmpty(pattern) || !Enum.TryParse<GapPattern>(pattern, ignoreCase: true, out var wanted))
        {
            return report;
        }

        var items = report.Items.Where(i => i.Pattern == wanted).ToArray();
        return new GapReport
        {
            GeneratedUtc = report.GeneratedUtc,
            GeneratedVersion = report.GeneratedVersion,
            TotalGaps = report.TotalGaps,
            Items = items
        };
    }

    /// <summary>
    /// Gets a lightweight overview of the report (per-pattern counts and provider names) without the gap
    /// items, so the dashboard can render the tabs and provider filter before loading any one tab.
    /// </summary>
    /// <returns>The report summary.</returns>
    [HttpGet("Summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<GapSummary> GetSummary()
    {
        var report = _store.LoadSnapshot();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var providers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var item in report.Items)
        {
            counts.TryGetValue(item.PatternName, out var c);
            counts[item.PatternName] = c + 1;

            foreach (var offer in item.Availability)
            {
                if (!string.IsNullOrEmpty(offer.Provider))
                {
                    providers.Add(offer.Provider);
                }
            }
        }

        var config = Plugin.RequireConfiguration();
        return new GapSummary
        {
            GeneratedUtc = report.GeneratedUtc,
            GeneratedVersion = report.GeneratedVersion,
            TotalGaps = report.TotalGaps,
            PatternCounts = counts,
            Providers = providers.ToArray(),
            AvailabilityEnabled = config.IncludeAvailability,
            AvailabilityPending = config.IncludeAvailability ? AvailabilityRunner.PendingTitleCount(report) : 0
        };
    }

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
    /// Re-checks a batch of owning items in the background, for the dashboard's per-heading re-check ("every
    /// studio", "every collection"). Runs in the background because a heading can cover hundreds of sets;
    /// poll <see cref="GetRecheckStatus"/> and reload the report when it finishes. The ownership index is
    /// built once for the whole batch, so this is cheaper than calling <see cref="RecheckSources"/> per item.
    /// </summary>
    /// <param name="ownerIds">The owning library items' ids (Jellyfin GUIDs). Ids no enabled source claims are skipped.</param>
    /// <returns>The run status, with Started indicating whether this call kicked one off.</returns>
    [HttpPost("RecheckSources")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<RecheckStatus> RecheckSources([FromBody] IReadOnlyList<string>? ownerIds)
    {
        if (ownerIds is null || ownerIds.Count == 0)
        {
            return BadRequest("At least one ownerId is required.");
        }

        var ids = new List<Guid>(ownerIds.Count);
        foreach (var raw in ownerIds)
        {
            if (Guid.TryParse(raw, out var id))
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            return BadRequest("No ownerId parsed as a library item id.");
        }

        var started = _recheckRunner.TryStart(ids);
        return RecheckStatusNow(started);
    }

    /// <summary>
    /// Gets whether a bulk re-check is running, and how far through it is.
    /// </summary>
    /// <returns>The bulk re-check status.</returns>
    [HttpGet("RecheckStatus")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<RecheckStatus> GetRecheckStatus() => RecheckStatusNow(false);

    private RecheckStatus RecheckStatusNow(bool started) => new()
    {
        Running = _recheckRunner.IsRunning,
        Started = started,
        Progress = _recheckRunner.Progress,
        Total = _recheckRunner.Total,
        Done = _recheckRunner.Done
    };

    /// <summary>
    /// Verifies gaps against the library and drops the ones it now holds, so a filled gap can be cleared from
    /// the report without a rescan. Purely local: it asks no provider, and so clears filled gaps but does not
    /// discover new ones (see <see cref="RecheckSources"/> for that). Works for every domain, pattern, and kind.
    /// </summary>
    /// <param name="ids">The gap ids to check. Ids the report no longer holds are skipped.</param>
    /// <returns>How many were checked, how many were owned, and the ids removed.</returns>
    [HttpPost("Verify")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<VerifyResult> Verify([FromBody] IReadOnlyList<string>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            return BadRequest("At least one gap id is required.");
        }

        // Rehydrate every gap server-side by id, as every other endpoint does: the client sends ids, never
        // gap bodies, so a stale or forged row cannot decide what gets dropped. One snapshot, indexed once
        // and reused for the sweep below, because a per-id scan of the whole report would be quadratic on a
        // large filtered tab before a single library query had run.
        var snapshot = _store.LoadSnapshot().Items;
        var byId = new Dictionary<string, GapItem>(snapshot.Count, StringComparer.Ordinal);
        foreach (var item in snapshot)
        {
            byId[item.Id] = item;
        }

        var checkedCount = 0;
        var owned = new List<GapItem>();
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var gap))
            {
                continue;
            }

            checkedCount++;
            if (_verifier.Owns(gap))
            {
                owned.Add(gap);
            }
        }

        // One acquired title is normally several gaps (its collection, a studio set, a filmography, a
        // recommendation), each with its own id from its own source. Having confirmed the title is in the
        // library, drop every gap about it, not just the row that was clicked, so it leaves all the tabs it
        // was on at once rather than lingering until each is verified separately.
        var removedIds = GapTargetKey.MatchingIds(snapshot, owned);
        var removed = _store.RemoveGaps(removedIds);

        return new VerifyResult
        {
            Checked = checkedCount,
            Owned = owned.Count,
            Removed = removed,
            RemovedIds = removedIds
        };
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
    /// Explores a by-id source ad-hoc against current library ownership, marks the produced gaps ad-hoc, and
    /// merges them additively into the report without a full rescan. The kind picks the source ("studio",
    /// "keyword", and "tmdblist" run TheMovieDb curated sets; "label" runs a Discogs label; "mdblist" runs
    /// an MDBList list), and ids is the picked ids for that kind. Runs in the background so a slow provider
    /// cannot time out the request; poll <see cref="GetExploreStatus"/> for completion, then reload the
    /// report.
    /// </summary>
    /// <param name="kind">The source kind: "studio", "keyword", "tmdblist", "label", or "mdblist".</param>
    /// <param name="ids">A comma-separated list (or single value) of ids to explore for that kind.</param>
    /// <returns>The explore status, with Started indicating whether this call kicked off a new explore.</returns>
    [HttpPost("Explore")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<ScanStatus> Explore([FromQuery] string? kind, [FromQuery] string? ids)
    {
        if (!_explore.IsKnown(kind))
        {
            return BadRequest(string.Create(
                CultureInfo.InvariantCulture,
                $"Unknown explore kind '{kind}'. Supported kinds: {string.Join(", ", _explore.KindTokens)}."));
        }

        var picked = ConfigIds.ParseInts(ids);
        if (picked.Count == 0)
        {
            return BadRequest("No valid ids to explore.");
        }

        var started = _exploreRunner.TryStartExplore(kind!, picked);
        return new ScanStatus { Running = true, Started = started, Progress = _exploreRunner.Progress };
    }

    /// <summary>
    /// Gets whether a background ad-hoc explore is currently running, and its progress.
    /// </summary>
    /// <returns>The explore status.</returns>
    [HttpGet("Explore/Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ScanStatus> GetExploreStatus()
        => new ScanStatus { Running = _exploreRunner.IsExploring, Progress = _exploreRunner.Progress };

    /// <summary>
    /// Clears the ad-hoc "explore a source" gaps from the report. With a source given, only that owning
    /// item's ad-hoc gaps are cleared; otherwise every ad-hoc gap is cleared. Permanent (scanned) gaps are
    /// left untouched.
    /// </summary>
    /// <param name="source">The owning item id to scope the clear to, or omitted to clear all ad-hoc gaps.</param>
    /// <returns>The number of ad-hoc gaps removed.</returns>
    [HttpPost("Explore/Clear")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<int> ClearExplore([FromQuery] string? source)
        => _store.RemoveAdhocGaps(source);

    /// <summary>
    /// Lists the chip-pickable explore kinds (token, label, and whether searchable) the registered sources
    /// declare, so the dashboard's "Explore a source" dropdown is derived rather than hard-coded.
    /// </summary>
    /// <returns>The explore kinds.</returns>
    [HttpGet("Explore/Kinds")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ExploreKindInfo>> ExploreKinds() => _explore.Kinds.ToList();

    /// <summary>
    /// Type-ahead for a chip-pickable explore kind: runs that kind's search so the settings page can offer
    /// matches to pick by name, never exposing the numeric id. Empty for a kind with no search (a TMDB list,
    /// entered by raw id) or an unknown kind.
    /// </summary>
    /// <param name="kind">The explore kind, for example "studio", "keyword", "label", or "mdblist".</param>
    /// <param name="query">The partial name typed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The top matches as id and name pairs.</returns>
    [HttpGet("CuratedSearch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CuratedSetRef>>> CuratedSearch([FromQuery] string? kind, [FromQuery] string? query, CancellationToken cancellationToken)
    {
        var search = _explore.Find(kind)?.Search;
        if (search is null || string.IsNullOrWhiteSpace(query))
        {
            return new List<CuratedSetRef>();
        }

        var results = await search(query, cancellationToken).ConfigureAwait(false);
        return results.ToList();
    }

    /// <summary>
    /// Resolves stored explore-kind ids to id and name pairs, so the settings page can render a chip per saved
    /// set with its name rather than its id. A kind with no resolve (a raw-id kind) keeps the id as the name.
    /// </summary>
    /// <param name="kind">The explore kind, for example "studio", "keyword", "label", or "mdblist".</param>
    /// <param name="ids">A comma-separated list of stored ids.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved sets, de-duplicated by id in input order.</returns>
    [HttpGet("CuratedResolve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CuratedSetRef>>> CuratedResolve([FromQuery] string? kind, [FromQuery] string? ids, CancellationToken cancellationToken)
    {
        var resolve = _explore.Find(kind)?.Resolve;
        var resolved = new List<CuratedSetRef>();

        foreach (var id in ConfigIds.ParseInts(ids))
        {
            var name = resolve is null ? null : await resolve(id, cancellationToken).ConfigureAwait(false);
            resolved.Add(new CuratedSetRef { Id = id, Name = string.IsNullOrEmpty(name) ? id.ToString(CultureInfo.InvariantCulture) : name });
        }

        return resolved;
    }
}
