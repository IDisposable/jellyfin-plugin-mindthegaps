using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using Jellyfin.Plugin.MindTheGaps.Services.Diagnostics;
using Jellyfin.Plugin.MindTheGaps.Services.Discogs;
using Jellyfin.Plugin.MindTheGaps.Services.MdbList;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Jellyfin.Plugin.MindTheGaps.VirtualItems;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
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
    private readonly ExploreRunner _exploreRunner;
    private readonly AvailabilityService _availabilityService;
    private readonly AvailabilityRunner _availabilityRunner;
    private readonly VirtualItemMinter _minter;
    private readonly MintRunner _mintRunner;
    private readonly ResolutionStore _resolutions;
    private readonly TodoStore _todo;
    private readonly ILibraryManager _libraryManager;
    private readonly ScanCursorStore _cursors;
    private readonly GapDiagnostics _diagnostics;
    private readonly TmdbClient _tmdb;
    private readonly DiscogsClient _discogs;
    private readonly MdbListClient _mdblist;
    private readonly AcquisitionService _acquisition;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapsController"/> class.
    /// </summary>
    /// <param name="store">The gap store.</param>
    /// <param name="scanRunner">The background scan runner.</param>
    /// <param name="exploreRunner">The background ad-hoc explore runner.</param>
    /// <param name="availabilityService">The availability service.</param>
    /// <param name="availabilityRunner">The background availability enrichment runner.</param>
    /// <param name="minter">The virtual-item minter.</param>
    /// <param name="mintRunner">The background mint runner.</param>
    /// <param name="resolutions">The store of per-gap resolution notes.</param>
    /// <param name="todo">The personal todo-list store.</param>
    /// <param name="libraryManager">The library manager, used to verify a todo entry against the library.</param>
    /// <param name="cursors">The scan-rotation cursor store.</param>
    /// <param name="diagnostics">The gap identification diagnostics.</param>
    /// <param name="tmdb">The TheMovieDb client, for the curated-set type-ahead and id resolution.</param>
    /// <param name="discogs">The Discogs client, for the curated-label type-ahead and id resolution.</param>
    /// <param name="mdblist">The MDBList client, for the MDBList list type-ahead and id resolution.</param>
    /// <param name="acquisition">The acquisition handoff service (Radarr/Sonarr/Jellyseerr).</param>
    public GapsController(GapStore store, GapScanRunner scanRunner, ExploreRunner exploreRunner, AvailabilityService availabilityService, AvailabilityRunner availabilityRunner, VirtualItemMinter minter, MintRunner mintRunner, ResolutionStore resolutions, TodoStore todo, ILibraryManager libraryManager, ScanCursorStore cursors, GapDiagnostics diagnostics, TmdbClient tmdb, DiscogsClient discogs, MdbListClient mdblist, AcquisitionService acquisition)
    {
        _store = store;
        _scanRunner = scanRunner;
        _exploreRunner = exploreRunner;
        _availabilityService = availabilityService;
        _availabilityRunner = availabilityRunner;
        _minter = minter;
        _mintRunner = mintRunner;
        _resolutions = resolutions;
        _todo = todo;
        _libraryManager = libraryManager;
        _cursors = cursors;
        _diagnostics = diagnostics;
        _tmdb = tmdb;
        _discogs = discogs;
        _mdblist = mdblist;
        _acquisition = acquisition;
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

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
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
        if (!GapEngine.IsExploreKind(kind))
        {
            return BadRequest(string.Create(
                CultureInfo.InvariantCulture,
                $"Unknown explore kind '{kind}'. Supported kinds: {string.Join(", ", GapEngine.ExploreKinds)}."));
        }

        var picked = ParseIds(ids).Distinct().ToList();
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
        var gap = RehydrateGap(id);
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
        var gap = RehydrateGap(id);
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
        var wanted = new HashSet<string>(ids ?? Array.Empty<string>(), StringComparer.Ordinal);
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
        var gap = RehydrateGap(id);
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

    /// <summary>
    /// Type-ahead for the curated-set settings: searches TheMovieDb studios or keywords by name so the
    /// settings page can offer matches to pick, never exposing the numeric id.
    /// </summary>
    /// <param name="kind">"studio" or "keyword".</param>
    /// <param name="query">The partial name typed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The top matches as id and name pairs.</returns>
    [HttpGet("CuratedSearch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CuratedSetRef>>> CuratedSearch([FromQuery] string? kind, [FromQuery] string? query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<CuratedSetRef>();
        }

        IReadOnlyList<CuratedSetRef> results;
        if (IsLabel(kind))
        {
            results = await _discogs.SearchLabelsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        else if (IsMdbList(kind))
        {
            results = await _mdblist.SearchListsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        else if (IsKeyword(kind))
        {
            results = await _tmdb.SearchKeywordsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            results = await _tmdb.SearchCompaniesAsync(query, cancellationToken).ConfigureAwait(false);
        }

        return results.ToList();
    }

    /// <summary>
    /// Resolves stored curated-set ids to id and name pairs, so the settings page can render a chip per
    /// saved set with its name rather than its id.
    /// </summary>
    /// <param name="kind">"studio", "keyword", or "label".</param>
    /// <param name="ids">A comma-separated list of stored ids.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved sets, de-duplicated by id in input order.</returns>
    [HttpGet("CuratedResolve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CuratedSetRef>>> CuratedResolve([FromQuery] string? kind, [FromQuery] string? ids, CancellationToken cancellationToken)
    {
        var keyword = IsKeyword(kind);
        var label = IsLabel(kind);
        var mdblist = IsMdbList(kind);
        var resolved = new List<CuratedSetRef>();
        var seen = new HashSet<int>();

        foreach (var id in ParseIds(ids))
        {
            if (!seen.Add(id))
            {
                continue;
            }

            string? name;
            if (label)
            {
                name = await _discogs.GetLabelNameAsync(id, cancellationToken).ConfigureAwait(false);
            }
            else if (mdblist)
            {
                name = await _mdblist.GetListNameAsync(id, cancellationToken).ConfigureAwait(false);
            }
            else if (keyword)
            {
                name = await _tmdb.GetKeywordNameAsync(id, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                name = await _tmdb.GetCompanyNameAsync(id, cancellationToken).ConfigureAwait(false);
            }

            resolved.Add(new CuratedSetRef { Id = id, Name = string.IsNullOrEmpty(name) ? id.ToString(CultureInfo.InvariantCulture) : name });
        }

        return resolved;
    }

    // Whether a curated-set kind query value means keywords (otherwise studios, unless it is a label).
    private static bool IsKeyword(string? kind)
        => string.Equals(kind, "keyword", StringComparison.OrdinalIgnoreCase);

    // Whether a curated-set kind query value means a Discogs label.
    private static bool IsLabel(string? kind)
        => string.Equals(kind, "label", StringComparison.OrdinalIgnoreCase);

    // Whether a curated-set kind query value means an MDBList list.
    private static bool IsMdbList(string? kind)
        => string.Equals(kind, "mdblist", StringComparison.OrdinalIgnoreCase);

    // Parse a comma-separated list of ids, ignoring blanks and non-numbers.
    private static IEnumerable<int> ParseIds(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<int>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
                .Where(id => id > 0);

    // Look a gap up by its stable id in the current stored report. Returns null for a missing/unknown id.
    private GapItem? RehydrateGap(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return _store.LoadSnapshot().Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));
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
    /// Dismisses several gaps at once with the same kind and note, for the "resolve every item under this
    /// series or season" group actions. Persists across rescans.
    /// </summary>
    /// <param name="request">The gap ids, the kind, and a note.</param>
    /// <returns>No content.</returns>
    [HttpPost("ResolveBatch")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult ResolveBatch([FromBody] ResolveBatchRequest request)
    {
        foreach (var id in request.Ids)
        {
            _resolutions.SetState(id, request.Kind, request.Note, null);
        }

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

    /// <summary>
    /// Gets the personal todo list (gaps the user marked to acquire), with the web-search URL template the
    /// dashboard builds each row's search link from.
    /// </summary>
    /// <returns>The todo list.</returns>
    [HttpGet("Todo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TodoList> GetTodo()
    {
        var config = Plugin.Instance?.Configuration;
        return new TodoList
        {
            Items = _todo.Load(),
            SearchUrlTemplate = config?.SearchUrlTemplate ?? new PluginConfiguration().SearchUrlTemplate,
            GeneratedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Adds the named report gaps to the personal todo list, snapshotting each server-side from the stored
    /// report by id (never trusting a client-posted gap body). Unknown ids are dropped; re-adding a title
    /// keeps its existing done state.
    /// </summary>
    /// <param name="ids">The stable ids of the report gaps to add.</param>
    /// <returns>The number of entries newly added.</returns>
    [HttpPost("Todo/Add")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<int> AddTodo([FromBody] IReadOnlyList<string> ids)
    {
        var wanted = new HashSet<string>(ids ?? Array.Empty<string>(), StringComparer.Ordinal);
        var gaps = _store.LoadSnapshot().Items.Where(i => wanted.Contains(i.Id)).ToList();
        return _todo.Add(gaps);
    }

    /// <summary>
    /// Removes an entry from the personal todo list.
    /// </summary>
    /// <param name="id">The entry id.</param>
    /// <returns>The number of entries removed (0 or 1).</returns>
    [HttpPost("Todo/Remove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<int> RemoveTodo([FromQuery] string id) => _todo.Remove(id);

    /// <summary>
    /// Sets a todo entry's done state.
    /// </summary>
    /// <param name="id">The entry id.</param>
    /// <param name="done">Whether the entry is done.</param>
    /// <returns>No content.</returns>
    [HttpPost("Todo/SetDone")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult SetTodoDone([FromQuery] string id, [FromQuery] bool done)
    {
        _todo.SetDone(id, done);
        return NoContent();
    }

    /// <summary>
    /// Verifies a todo entry against the library: whether a real (non-virtual) item of the entry's kind now
    /// carries any of its provider ids. Marks the entry done to match, and returns the outcome with the
    /// updated entry.
    /// </summary>
    /// <param name="id">The entry id.</param>
    /// <returns>Whether the library owns the entry, and the entry with its done state updated.</returns>
    [HttpPost("Todo/Verify")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TodoVerifyResult> VerifyTodo([FromQuery] string id)
    {
        var entry = _todo.Load().FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
        if (entry is null)
        {
            return new TodoVerifyResult { Owned = false, Entry = null };
        }

        var owned = LibraryOwns(entry);
        _todo.SetDone(entry.Id, owned);
        entry.Done = owned;

        // Reload so the returned entry carries the freshly stamped/cleared done timestamp.
        var updated = _todo.Load().FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal)) ?? entry;
        return new TodoVerifyResult { Owned = owned, Entry = updated };
    }

    // Whether the library owns a real (non-virtual) item of the entry's kind carrying any of its provider
    // ids. A focused query (the kind, real items only, any of the ids) keeps the lookup cheap rather than
    // building a whole ownership index for a single check.
    private bool LibraryOwns(TodoEntry entry)
    {
        if (entry.ProviderIds.Count == 0
            || !Enum.TryParse<BaseItemKind>(entry.TargetKindName, ignoreCase: false, out var kind))
        {
            return false;
        }

        var hasAny = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in entry.ProviderIds)
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                hasAny[pair.Key] = pair.Value;
            }
        }

        if (hasAny.Count == 0)
        {
            return false;
        }

        var matches = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { kind },
            IsVirtualItem = false,
            HasAnyProviderId = hasAny,
            Recursive = true
        });

        return matches.Count > 0;
    }
}
