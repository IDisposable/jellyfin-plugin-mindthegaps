using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.VirtualItems;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.ScheduledTasks;

/// <summary>
/// Opt-in scheduled task that mints virtual placeholders for new materializable gaps in the patterns chosen
/// in settings. Off by default and with no default trigger, so it runs only once an admin turns it on and
/// schedules it. Reconciliation of placeholders the library now owns for real runs at the end of every scan,
/// independent of this task, so this only adds; the per-run cap keeps it from flooding a library.
/// </summary>
public sealed class AutoMintTask : IScheduledTask
{
    private readonly GapStore _store;
    private readonly VirtualMovieMinter _minter;
    private readonly ILogger<AutoMintTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoMintTask"/> class.
    /// </summary>
    /// <param name="store">The gap store (the source of gaps to mint).</param>
    /// <param name="minter">The virtual-movie minter.</param>
    /// <param name="logger">The logger.</param>
    public AutoMintTask(GapStore store, VirtualMovieMinter minter, ILogger<AutoMintTask> logger)
    {
        _store = store;
        _minter = minter;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Auto-mint gaps";

    /// <inheritdoc />
    public string Key => "MindTheGapsAutoMint";

    /// <inheritdoc />
    public string Description => "When enabled in settings, mints virtual placeholders for new materializable gaps in the selected patterns. Off by default.";

    /// <inheritdoc />
    public string Category => "Mind the Gaps";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!config.AutoMint)
        {
            _logger.LogDebug("Auto-mint is off; nothing to do");
            progress.Report(100);
            return;
        }

        var patterns = SelectedPatterns(config);
        if (patterns.Count == 0)
        {
            _logger.LogInformation("Auto-mint is on but no patterns are selected; nothing to do");
            progress.Report(100);
            return;
        }

        var report = _store.LoadSnapshot();
        var message = await _minter.MintMatchingAsync(
            report.Items,
            gap => patterns.Contains(gap.Pattern),
            config.AutoMintCap,
            dryRun: false,
            progress,
            cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Auto-mint complete: {Message}", message);
        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // No default trigger: the task is opt-in, so it only runs once an admin enables auto-mint and adds a
        // schedule for it.
        yield break;
    }

    private static HashSet<GapPattern> SelectedPatterns(PluginConfiguration config)
    {
        var patterns = new HashSet<GapPattern>();
        if (config.AutoMintSetCompletion)
        {
            patterns.Add(GapPattern.SetCompletion);
        }

        if (config.AutoMintCreatorWorks)
        {
            patterns.Add(GapPattern.CreatorWorks);
        }

        if (config.AutoMintRecommendations)
        {
            patterns.Add(GapPattern.Recommendation);
        }

        return patterns;
    }
}
