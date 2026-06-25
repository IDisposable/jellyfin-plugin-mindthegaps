using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Services.Availability;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.ScheduledTasks;

/// <summary>
/// Scheduled task that drains the "where to watch" backlog, so the dashboard's availability stays current
/// without anyone clicking the report's "Look up where to watch" button. Unlike that button, which does one
/// capped batch per press, this drains every pending title in a run. It no-ops when availability is turned
/// off, or when a manual pass is already running.
/// </summary>
public sealed class AvailabilityRefreshTask : IScheduledTask
{
    private readonly AvailabilityRunner _runner;
    private readonly ILogger<AvailabilityRefreshTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvailabilityRefreshTask"/> class.
    /// </summary>
    /// <param name="runner">The availability enrichment runner.</param>
    /// <param name="logger">The logger.</param>
    public AvailabilityRefreshTask(AvailabilityRunner runner, ILogger<AvailabilityRefreshTask> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Refresh where to watch";

    /// <inheritdoc />
    public string Key => "MindTheGapsAvailabilityRefresh";

    /// <inheritdoc />
    public string Description => "Looks up streaming availability for every gap still missing it, keeping the report's 'where to watch' data current.";

    /// <inheritdoc />
    public string Category => "Mind the Gaps";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.RequireConfiguration();
        if (!config.IncludeAvailability)
        {
            _logger.LogWarning("Availability refresh skipped: 'where to watch' is turned off");
            return;
        }

        await _runner.EnrichAllAsync(progress, cancellationToken).ConfigureAwait(false);
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
