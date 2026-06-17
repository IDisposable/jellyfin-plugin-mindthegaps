using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.VirtualItems;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.ScheduledTasks;

/// <summary>
/// Scheduled task that rebuilds the gaps todo list.
/// </summary>
public sealed class GapScanTask : IScheduledTask
{
    private readonly GapEngine _engine;
    private readonly VirtualMovieMinter _minter;
    private readonly ILogger<GapScanTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapScanTask"/> class.
    /// </summary>
    /// <param name="engine">The gap engine.</param>
    /// <param name="minter">The minter (reconciles minted placeholders now owned for real).</param>
    /// <param name="logger">The logger.</param>
    public GapScanTask(GapEngine engine, VirtualMovieMinter minter, ILogger<GapScanTask> logger)
    {
        _engine = engine;
        _minter = minter;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Scan for collection gaps";

    /// <inheritdoc />
    public string Key => "MindTheGapsScan";

    /// <inheritdoc />
    public string Description => "Scans the library for missing and related content and rebuilds the gaps todo list.";

    /// <inheritdoc />
    public string Category => "Mind the Gaps";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var report = await _engine.RunAsync(progress, cancellationToken).ConfigureAwait(false);
        _minter.ReconcileMinted();
        _logger.LogInformation("Collection gap scan complete: {Count} gaps", report.TotalGaps);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(24).Ticks
        };
    }
}
