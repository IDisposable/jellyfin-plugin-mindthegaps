using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Orchestrates a gap scan: builds a library snapshot, runs every enabled source,
/// de-duplicates, and persists the resulting todo list.
/// </summary>
public sealed class GapEngine
{
    private readonly ILibraryManager _libraryManager;
    private readonly IEnumerable<IGapSource> _sources;
    private readonly GapStore _store;
    private readonly ILogger<GapEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapEngine"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="sources">The registered gap sources.</param>
    /// <param name="store">The gap store.</param>
    /// <param name="logger">The logger.</param>
    public GapEngine(
        ILibraryManager libraryManager,
        IEnumerable<IGapSource> sources,
        GapStore store,
        ILogger<GapEngine> logger)
    {
        _libraryManager = libraryManager;
        _sources = sources;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Runs all enabled sources and saves the resulting report.
    /// </summary>
    /// <param name="progress">Optional progress reporter (0-100).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generated report.</returns>
    public async Task<GapReport> RunAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var enabled = _sources.Where(s => s.IsEnabled(config)).ToList();
        var context = BuildContext(enabled, config);

        var gaps = new List<GapItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var total = enabled.Count;
        var completed = 0;

        foreach (var source in enabled)
        {
            try
            {
                await foreach (var gap in source.FindGapsAsync(context, cancellationToken).ConfigureAwait(false))
                {
                    if (seen.Add(gap.Id))
                    {
                        gaps.Add(gap);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gap source {Source} failed", source.Name);
            }

            completed++;
            progress?.Report(completed * 100.0 / total);
        }

        var report = new GapReport
        {
            GeneratedUtc = DateTime.UtcNow,
            TotalGaps = gaps.Count,
            Items = gaps
        };

        _store.Save(report);
        return report;
    }

    private GapScanContext BuildContext(IReadOnlyCollection<IGapSource> enabledSources, PluginConfiguration config)
    {
        // The kinds to index are declared by the sources themselves; the engine just unions them.
        var kinds = enabledSources.SelectMany(s => s.OwnedKinds).Distinct().ToArray();
        var byKey = new Dictionary<string, BaseItem>();

        if (kinds.Length > 0)
        {
            foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = kinds,
                Recursive = true
            }))
            {
                var kind = item.GetBaseItemKind();
                foreach (var providerId in item.ProviderIds)
                {
                    if (!string.IsNullOrEmpty(providerId.Value))
                    {
                        byKey[OwnershipIndex.MakeKey(kind, providerId.Key, providerId.Value)] = item;
                    }
                }
            }
        }

        var ownership = new OwnershipIndex(byKey);
        _logger.LogInformation(
            "Library snapshot: {Count} owned provider-id keys across kinds [{Kinds}]",
            ownership.Count,
            string.Join(", ", kinds));

        return new GapScanContext(config, ownership);
    }
}
