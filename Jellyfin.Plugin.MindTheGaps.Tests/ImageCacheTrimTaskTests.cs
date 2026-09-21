using System;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.ScheduledTasks;
using Jellyfin.Plugin.MindTheGaps.Services.Images;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public sealed class ImageCacheTrimTaskTests : IDisposable
{
    private readonly PluginLifetime _lifetime = new();
    private readonly ImageCache _cache;
    private readonly ImageCacheTrimTask _task;

    public ImageCacheTrimTaskTests()
    {
        _cache = new ImageCache(
            new FakeImageHost(_ => new System.Net.Http.HttpResponseMessage()),
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mtg-trim-task-" + Guid.NewGuid().ToString("N")),
            new MemoryCache(new MemoryCacheOptions()),
            _lifetime,
            NullLogger<ImageCache>.Instance,
            new ImageHostHealth());
        _task = new ImageCacheTrimTask(_cache, NullLogger<ImageCacheTrimTask>.Instance);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _lifetime.Dispose();
    }

    [Fact]
    public void RunsWhenTheServerStartsAndOnceADayByDefault()
    {
        var triggers = _task.GetDefaultTriggers().ToList();

        Assert.Equal(2, triggers.Count);
        Assert.Contains(triggers, t => t.Type == TaskTriggerInfoType.StartupTrigger);
        Assert.Contains(triggers, t => t.Type == TaskTriggerInfoType.IntervalTrigger && t.IntervalTicks == TimeSpan.FromHours(24).Ticks);
    }

    [Fact]
    public void IsAMindTheGapsTaskWithAStableKey()
    {
        Assert.Equal("Mind the Gaps", _task.Category);
        Assert.Equal("MindTheGapsImageCacheTrim", _task.Key);
        Assert.False(string.IsNullOrWhiteSpace(_task.Name));
        Assert.False(string.IsNullOrWhiteSpace(_task.Description));
    }
}
