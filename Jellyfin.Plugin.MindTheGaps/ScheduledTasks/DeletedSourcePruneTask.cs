using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.ScheduledTasks;

/// <summary>
/// Scheduled task that takes the library items the library no longer holds out of the report's gap sources
/// (see <see cref="GapStore.PruneDeletedSources"/>). It has no default trigger: an administrator runs it from
/// the scheduled tasks page, or gives it a schedule there. It waits for a running scan to finish, since the
/// scan would save over the prune with the report it read before it.
/// </summary>
public sealed class DeletedSourcePruneTask : IScheduledTask
{
    private readonly GapStore _store;
    private readonly GapScanGate _scanGate;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<DeletedSourcePruneTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeletedSourcePruneTask"/> class.
    /// </summary>
    /// <param name="store">The gap store.</param>
    /// <param name="scanGate">Held for the run, so no scan overlaps it.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public DeletedSourcePruneTask(GapStore store, GapScanGate scanGate, ILibraryManager libraryManager, ILogger<DeletedSourcePruneTask> logger)
    {
        _store = store;
        _scanGate = scanGate;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Prune gaps from deleted items";

    /// <inheritdoc />
    public string Key => "MindTheGapsDeletedSourcePrune";

    /// <inheritdoc />
    public string Description => "Removes deleted library items as the source of a gap, moving the gap to its next source or removing it when it has none. Waits for a running scan to finish.";

    /// <inheritdoc />
    public string Category => "Mind the Gaps";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        using (await _scanGate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            Prune(progress, cancellationToken);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    private void Prune(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var sourceIds = DeletedSourcePruner.LibrarySourceIds(_store.LoadSnapshot().Items);
        var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // An empty ItemIds is no filter at all, which would read every id in the library.
        if (sourceIds.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Ids only and no user, so core applies no type, virtual-item, or grouping filter: an id that
            // comes back is an item the library holds, whatever its kind.
            var present = _libraryManager.GetItemIds(new InternalItemsQuery
            {
                ItemIds = sourceIds.ToArray(),
                DtoOptions = LibraryQueryOptions.Minimal()
            }).ToHashSet();
            foreach (var id in sourceIds)
            {
                if (!present.Contains(id))
                {
                    deleted.Add(id.ToString("N", CultureInfo.InvariantCulture));
                }
            }
        }

        progress.Report(90);
        var (removed, rewritten) = _store.PruneDeletedSources(deleted);
        progress.Report(100);
        _logger.LogInformation(
            "Gap sources: {Checked} library item(s) checked, {Deleted} deleted; removed {Removed} gap(s) and dropped a source from {Rewritten} more",
            sourceIds.Count,
            deleted.Count,
            removed,
            rewritten);
    }
}
