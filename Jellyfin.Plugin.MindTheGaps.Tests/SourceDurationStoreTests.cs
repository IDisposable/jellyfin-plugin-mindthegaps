using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public sealed class SourceDurationStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mtg-durations-" + Guid.NewGuid().ToString("N"));

    private SourceDurationStore Store() => new(NullLogger<SourceDurationStore>.Instance, _dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void AnswersInTheOrderAskedWithNullForAnUnknownSource()
    {
        var store = Store();
        store.Record(new Dictionary<string, double> { ["Series"] = 222, ["People"] = 110 });

        Assert.Equal([110, null, 222], store.Get(["People", "New", "Series"]));
    }

    [Fact]
    public void SurvivesARestart()
    {
        Store().Record(new Dictionary<string, double> { ["People"] = 110.04 });

        Assert.Equal([110.0], Store().Get(["People"]));
    }

    [Fact]
    public void ReplacesARecordedRunAndKeepsTheOthers()
    {
        var store = Store();
        store.Record(new Dictionary<string, double> { ["People"] = 110, ["Series"] = 222 });
        store.Record(new Dictionary<string, double> { ["People"] = 90 });

        Assert.Equal([90, 222], store.Get(["People", "Series"]));
    }

    [Fact]
    public void AnUnreadableFileReadsAsNoHistory()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "scan-durations.json"), "not json");

        Assert.Equal([null], Store().Get(["People"]));
    }
}
