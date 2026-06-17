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

    private static bool NeedsLookup(GapItem gap)
        => gap.Availability.Count == 0
            && gap.TargetKind is BaseItemKind.Movie or BaseItemKind.Series
            && gap.ProviderIds.ContainsKey(GapScanContext.TmdbProvider);

    private async Task RunAsync()
    {
        try
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var report = _store.Load();
            var pending = report.Items.Where(NeedsLookup).ToList();
            var batch = pending.Take(GapScanLimits.MaxAvailabilityLookups).ToList();

            if (batch.Count == 0)
            {
                SetMessage("Every watchable gap already has 'where to watch' data.");
                return;
            }

            _logger.LogInformation("Availability enrichment started: {Batch} to look up ({Pending} pending)", batch.Count, pending.Count);

            var enriched = 0;
            for (var i = 0; i < batch.Count; i++)
            {
                var gap = batch[i];
                await ResolveExternalIdsAsync(gap).ConfigureAwait(false);

                try
                {
                    var offers = await _availabilityService.GetOffersAsync(
                        new AvailabilityQuery
                        {
                            TargetKind = gap.TargetKind,
                            ProviderIds = gap.ProviderIds,
                            Title = gap.Name,
                            Year = gap.Year,
                            Country = config.MetadataCountryCode
                        },
                        config,
                        CancellationToken.None).ConfigureAwait(false);

                    if (offers.Count > 0)
                    {
                        gap.Availability = offers;
                        enriched++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Availability lookup failed for '{Name}'", gap.Name);
                }

                lock (_lock)
                {
                    _progress = (i + 1) * 100.0 / batch.Count;
                }

                if ((i + 1) % SaveEvery == 0)
                {
                    _store.SaveThrottled(report);
                }

                await Task.Delay(ThrottleMilliseconds).ConfigureAwait(false);
            }

            _store.Save(report);

            var remaining = pending.Count - batch.Count;
            var message = remaining > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Looked up {batch.Count} ({enriched} have sources); {remaining} remaining, run again to continue.")
                : string.Create(CultureInfo.InvariantCulture, $"Looked up {batch.Count} ({enriched} have sources). All caught up.");
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
