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
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Services.Availability;

/// <summary>
/// Enriches the persisted gap report in the background, so the dashboard's "Hide items with no
/// sources" filter (and richer external links) get data without the scan having to block on a long run
/// of network calls. For each watchable gap that has no offers yet it resolves the title's external ids
/// (so the row links to IMDb/TheTVDB, not just TMDB) and looks up "where to watch". It works in
/// batches, saving the report as it goes so results appear progressively, and stops at
/// <see cref="GapScanLimits.MaxAvailabilityLookups"/> per pass (re-running continues where it left off,
/// since enriched gaps are skipped). Only one pass runs at a time.
/// </summary>
public sealed class AvailabilityRunner
{
    // Persist every so often during a pass so the dashboard sees partial results without waiting for the end.
    private const int SaveEvery = 25;

    // A small courtesy delay between lookups so a pass does not burst the provider.
    private const int ThrottleMilliseconds = 20;

    private readonly GapStore _store;
    private readonly AvailabilityService _availabilityService;
    private readonly TmdbClient _tmdb;
    private readonly ExternalLinkEnricher _externalLinks;
    private readonly ILogger<AvailabilityRunner> _logger;
    private readonly object _lock = new();
    private bool _running;
    private double _progress;
    private string? _lastMessage;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvailabilityRunner"/> class.
    /// </summary>
    /// <param name="store">The gap store.</param>
    /// <param name="availabilityService">The availability service.</param>
    /// <param name="tmdb">The TMDB client (used to resolve external ids).</param>
    /// <param name="externalLinks">Folds the host's external-url providers into each gap's links.</param>
    /// <param name="logger">The logger.</param>
    public AvailabilityRunner(GapStore store, AvailabilityService availabilityService, TmdbClient tmdb, ExternalLinkEnricher externalLinks, ILogger<AvailabilityRunner> logger)
    {
        _store = store;
        _availabilityService = availabilityService;
        _tmdb = tmdb;
        _externalLinks = externalLinks;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether a pass is currently running.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _running;
            }
        }
    }

    /// <summary>
    /// Gets the progress (0-100) of the running pass.
    /// </summary>
    public double Progress
    {
        get
        {
            lock (_lock)
            {
                return _progress;
            }
        }
    }

    /// <summary>
    /// Gets the message from the last completed pass.
    /// </summary>
    public string? LastMessage
    {
        get
        {
            lock (_lock)
            {
                return _lastMessage;
            }
        }
    }

    /// <summary>
    /// Starts an enrichment pass in the background if one is not already running.
    /// </summary>
    /// <returns><see langword="true"/> if this call started a pass; <see langword="false"/> if one was already running.</returns>
    public bool TryStart()
    {
        lock (_lock)
        {
            if (_running)
            {
                return false;
            }

            _running = true;
            _progress = 0;
        }

        _ = Task.Run(RunAsync);
        return true;
    }

    // The (TMDB id, kind) to look up "where to watch" for a gap: a Movie/Series uses its own TMDB id; an
    // episode uses its owning series' id (looked up as a series). Null when there is nothing to look up.
    private static (string TmdbId, BaseItemKind Kind)? WatchTarget(GapItem gap)
    {
        if (gap.TargetKind is BaseItemKind.Movie or BaseItemKind.Series
            && gap.ProviderIds.TryGetValue(GapScanContext.TmdbProvider, out var id)
            && !string.IsNullOrEmpty(id))
        {
            return (id, gap.TargetKind);
        }

        if (gap.TargetKind is BaseItemKind.Episode && !string.IsNullOrEmpty(gap.WatchTmdbId))
        {
            return (gap.WatchTmdbId, BaseItemKind.Series);
        }

        return null;
    }

    private static bool NeedsLookup(GapItem gap) => !gap.AvailabilityChecked && WatchTarget(gap) is not null;

    // A stable grouping key for a gap's watch target, so gaps that resolve to the same title (every
    // episode of one series) share a single lookup.
    private static string WatchKey(GapItem gap)
    {
        var target = WatchTarget(gap)!.Value;
        return string.Create(CultureInfo.InvariantCulture, $"{target.Kind}:{target.TmdbId}");
    }

    private async Task RunAsync()
    {
        try
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var report = _store.Load();
            var pending = report.Items.Where(NeedsLookup).ToList();

            // Group by the watch target so every episode of a series shares a single lookup, and the
            // per-pass cap bounds distinct titles rather than rows.
            var groups = pending.GroupBy(WatchKey).ToList();
            var batch = groups.Take(GapScanLimits.MaxAvailabilityLookups).ToList();

            if (batch.Count == 0)
            {
                SetMessage("Every watchable gap has been checked for 'where to watch'.");
                return;
            }

            _logger.LogInformation("Availability enrichment started: {Batch} titles to look up ({Pending} gaps pending)", batch.Count, pending.Count);

            var enriched = 0;
            for (var i = 0; i < batch.Count; i++)
            {
                var group = batch[i];
                var first = group.First();
                var target = WatchTarget(first)!.Value;

                IReadOnlyList<AvailabilityOffer> offers = Array.Empty<AvailabilityOffer>();
                var looked = false;
                try
                {
                    offers = await _availabilityService.GetOffersAsync(
                        new AvailabilityQuery
                        {
                            TargetKind = target.Kind,
                            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [GapScanContext.TmdbProvider] = target.TmdbId },
                            Title = first.Name,
                            Year = first.Year,
                            Country = config.MetadataCountryCode
                        },
                        config,
                        CancellationToken.None).ConfigureAwait(false);
                    looked = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Availability lookup failed for '{Name}'", first.Name);
                }

                if (looked)
                {
                    if (offers.Count > 0)
                    {
                        enriched++;
                    }

                    foreach (var gap in group)
                    {
                        // Movie/Series gaps also pick up their IMDb/TheTVDB ids here; episodes already carry theirs.
                        await ResolveExternalIdsAsync(gap).ConfigureAwait(false);

                        // Marked checked on a successful lookup even with no offers, so the row shows "no
                        // sources" rather than a look-up button that comes back empty. A failed lookup leaves
                        // the gap unchecked, so it retries on the next pass.
                        gap.AvailabilityChecked = true;
                        if (offers.Count > 0)
                        {
                            gap.Availability = offers;
                        }
                    }
                }

                lock (_lock)
                {
                    _progress = (i + 1) * 100.0 / batch.Count;
                }

                if ((i + 1) % SaveEvery == 0)
                {
                    _store.SaveAvailabilityMerge(report, throttle: true);
                }

                await Task.Delay(ThrottleMilliseconds).ConfigureAwait(false);
            }

            _store.SaveAvailabilityMerge(report, throttle: false);

            var remaining = groups.Count - batch.Count;
            var message = remaining > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Looked up {batch.Count} titles ({enriched} have sources); {remaining} remaining, run again to continue.")
                : string.Create(CultureInfo.InvariantCulture, $"Looked up {batch.Count} titles ({enriched} have sources). All caught up.");
            SetMessage(message);
            _logger.LogInformation("Availability enrichment finished: {Message}", message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Availability enrichment failed");
            SetMessage("Availability lookup failed. Check the server logs.");
        }
        finally
        {
            lock (_lock)
            {
                _running = false;
            }
        }
    }

    // Resolve the title's IMDb/TheTVDB ids from its TMDB id and fold them in, then rebuild the gap's
    // links so the row shows more than the TMDB button. No-op when nothing new is found.
    private async Task ResolveExternalIdsAsync(GapItem gap)
    {
        // Only a movie/series gap's own TMDB id maps to an external-id lookup. An episode's TMDB id (if it
        // carries one) is an episode id, not a movie/series id, so resolving it would be wrong; episodes
        // already carry their TheTVDB/IMDb ids from the library.
        if (gap.TargetKind is not (BaseItemKind.Movie or BaseItemKind.Series))
        {
            return;
        }

        if (!gap.ProviderIds.TryGetValue(GapScanContext.TmdbProvider, out var tmdbStr)
            || !int.TryParse(tmdbStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
        {
            return;
        }

        (string? Imdb, string? Tvdb) ids;
        try
        {
            ids = await _tmdb.GetExternalIdsAsync(tmdbId, gap.TargetKind == BaseItemKind.Series, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External-id lookup failed for '{Name}'", gap.Name);
            return;
        }

        var merged = new Dictionary<string, string>(gap.ProviderIds, StringComparer.OrdinalIgnoreCase);
        var added = false;
        if (!string.IsNullOrEmpty(ids.Imdb) && !merged.ContainsKey("Imdb"))
        {
            merged["Imdb"] = ids.Imdb;
            added = true;
        }

        if (!string.IsNullOrEmpty(ids.Tvdb) && !merged.ContainsKey("Tvdb"))
        {
            merged["Tvdb"] = ids.Tvdb;
            added = true;
        }

        if (!added)
        {
            return;
        }

        gap.ProviderIds = merged;
        // Keep any existing links (for example Trakt) and add the fallback links the new ids imply, then
        // let the host's providers contribute (and win) on top.
        gap.Links = ExternalLinkEnricher.Merge(gap.Links, ProviderLinks.Build(gap.TargetKind, merged));
        _externalLinks.Enrich(new[] { gap });
    }

    private void SetMessage(string message)
    {
        lock (_lock)
        {
            _lastMessage = message;
        }
    }
}
