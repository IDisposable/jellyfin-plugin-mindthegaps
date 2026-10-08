using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.ScheduledTasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public sealed class DeletedSourcePruneTaskTests : IDisposable
{
    private const string Kept = "11111111111111111111111111111111";
    private const string Deleted = "22222222222222222222222222222222";
    private const string AlsoDeleted = "33333333333333333333333333333333";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mtg-prune-task-" + Guid.NewGuid().ToString("N"));
    private readonly GapStore _store;
    private readonly GapScanGate _gate = new();
    private readonly LibraryProxy _library;
    private readonly DeletedSourcePruneTask _task;

    public DeletedSourcePruneTaskTests()
    {
        _store = new GapStore(NullLogger<GapStore>.Instance, _dir);
        (_library, var service) = LibraryProxy.Create(Guid.ParseExact(Kept, "N"));
        _task = new DeletedSourcePruneTask(_store, _gate, service, NullLogger<DeletedSourcePruneTask>.Instance);
    }

    public void Dispose()
    {
        _gate.Dispose();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void HasNoDefaultSchedule()
    {
        Assert.Empty(_task.GetDefaultTriggers());
        Assert.Equal("Mind the Gaps", _task.Category);
        Assert.Equal("MindTheGapsDeletedSourcePrune", _task.Key);
    }

    [Fact]
    public async Task Execute_PrunesDeletedSourcesAndLeavesTheRestAlone()
    {
        var generated = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        _store.Save(new GapReport
        {
            GeneratedUtc = generated,
            Items =
            [
                Gap("owned", Kept),
                Gap("orphan", Deleted),
                Gap("promoted", Deleted, new GapSourceRef { Id = Kept, Name = "Kept" }),
                Gap("trimmed", Kept, new GapSourceRef { Id = AlsoDeleted, Name = "Gone" }),
                Gap("list", "imdblist-ls123")
            ]
        });

        await _task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        // One library read for every source, asking only about the library item ids, as no user.
        var query = Assert.Single(_library.Queries);
        Assert.Null(query.User);
        Assert.Equal(
            new[] { Kept, Deleted, AlsoDeleted }.Select(id => Guid.ParseExact(id, "N")).Order(),
            query.ItemIds.Order());

        var loaded = _store.Load();
        Assert.Equal(["owned", "promoted", "trimmed", "list"], loaded.Items.Select(i => i.Id));
        Assert.Equal(4, loaded.TotalGaps);
        Assert.Equal(generated, loaded.GeneratedUtc);

        var promoted = loaded.Items.Single(i => i.Id == "promoted");
        Assert.Equal(Kept, promoted.SourceItemId);
        Assert.Null(promoted.OtherSources);
        Assert.Null(loaded.Items.Single(i => i.Id == "trimmed").OtherSources);
    }

    [Fact]
    public async Task Execute_WithNoLibrarySources_NeverAsksTheLibrary()
    {
        // An empty id filter is no filter, so asking would read every id in the library.
        var report = new GapReport { Items = [Gap("list", "imdblist-ls123")] };
        _store.Save(report);

        await _task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Empty(_library.Queries);
        Assert.Same(report, _store.Load());
    }

    [Fact]
    public async Task Execute_WaitsForARunningScanToFinish()
    {
        var report = new GapReport { Items = [Gap("orphan", Deleted)] };
        _store.Save(report);

        var scan = await _gate.EnterAsync(CancellationToken.None);
        var prune = _task.ExecuteAsync(new Progress<double>(), CancellationToken.None);
        await Task.Delay(100);

        Assert.False(prune.IsCompleted);
        Assert.Same(report, _store.Load());

        scan.Dispose();
        await prune;
        Assert.Empty(_store.Load().Items);
    }

    [Fact]
    public async Task Execute_StopsWaitingWhenCancelled()
    {
        using var scan = await _gate.EnterAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource();

        var prune = _task.ExecuteAsync(new Progress<double>(), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prune);
    }

    [Fact]
    public void PruneDeletedSources_WithNothingToDo_LeavesTheReportAlone()
    {
        var report = new GapReport { Items = [Gap("owned", Kept)] };
        _store.Save(report);

        Assert.Equal((0, 0), _store.PruneDeletedSources(new HashSet<string> { Deleted }));
        Assert.Equal((0, 0), _store.PruneDeletedSources(new HashSet<string>()));
        Assert.Same(report, _store.Load());
    }

    private static GapItem Gap(string id, string sourceId, params GapSourceRef[] others) => new()
    {
        Id = id,
        Name = id,
        Pattern = GapPattern.Recommendation,
        SourceItemId = sourceId,
        SourceItemName = sourceId,
        OtherSources = others.Length == 0 ? null : others
    };

    // Answers GetItemIds the way core does for a query carrying only ids: the ones that exist. Any other
    // lookup returns nothing, so a per-id fallback would read every source as deleted and fail the tests.
    private class LibraryProxy : DispatchProxy
    {
        private HashSet<Guid> _present = [];

        public List<InternalItemsQuery> Queries { get; } = [];

        public static (LibraryProxy Control, ILibraryManager Service) Create(params Guid[] present)
        {
            var proxy = (LibraryProxy)(object)DispatchProxy.Create<ILibraryManager, LibraryProxy>();
            proxy._present = [.. present];
            return (proxy, (ILibraryManager)(object)proxy);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ILibraryManager.GetItemIds) && args?[0] is InternalItemsQuery query)
            {
                Queries.Add(query);
                return query.ItemIds.Where(_present.Contains).ToList();
            }

            return targetMethod?.ReturnType == typeof(void)
                ? null
                : targetMethod?.ReturnType.IsValueType == true
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null;
        }
    }
}
