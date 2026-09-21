using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Services.Images;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.ScheduledTasks;

/// <summary>
/// Scheduled task that keeps the image cache to its configured size, deleting the oldest images once it is over.
/// It is the only thing that trims: the request path never does, so serving an image cannot wait on a pass over
/// the folder. It also measures the folder, which is what the cache's ceiling on growth between runs is judged
/// against, so it runs when the server starts as well as daily.
/// </summary>
public sealed class ImageCacheTrimTask : IScheduledTask
{
    private readonly ImageCache _cache;
    private readonly ILogger<ImageCacheTrimTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCacheTrimTask"/> class.
    /// </summary>
    /// <param name="cache">The image cache.</param>
    /// <param name="logger">The logger.</param>
    public ImageCacheTrimTask(ImageCache cache, ILogger<ImageCacheTrimTask> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Trim the image cache";

    /// <inheritdoc />
    public string Key => "MindTheGapsImageCacheTrim";

    /// <inheritdoc />
    public string Description => "Deletes the oldest cached images once the image cache is over its configured size.";

    /// <inheritdoc />
    public string Category => "Mind the Gaps";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var cap = ImageCache.CapBytes(Plugin.RequireConfiguration());
        var result = await Task.Run(() => _cache.Trim(cap, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
        progress.Report(100);
        _logger.LogInformation(
            "Image cache: {Files} file(s), {Megabytes} MB; deleted {Deleted} to stay under {Cap} MB, {After} MB left",
            result.Files,
            result.BytesBefore / (1024 * 1024),
            result.Deleted,
            cap / (1024 * 1024),
            result.BytesAfter / (1024 * 1024));
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };

        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(24).Ticks
        };
    }
}
