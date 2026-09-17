using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Api;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class TodoControllerTests
{
    [Fact]
    public void AddTodo_UsesOnlyIdsFromTheCurrentReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            reportStore.Save(new GapReport
            {
                GeneratedUtc = DateTime.UtcNow,
                TotalGaps = 1,
                Items = [new GapItem
                {
                    Id = "known",
                    Name = "Known",
                    TargetKind = BaseItemKind.Movie,
                    ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1" }
                }]
            });
            var controller = new TodoController(reportStore, todoStore, null!);

            var result = controller.AddTodo(new[] { "known", "forged", "known" });

            Assert.Equal(1, result.Value);
            var entry = Assert.Single(todoStore.Load());
            Assert.Equal("known", entry.Id);
            Assert.Equal("Known", entry.Name);
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    [Fact]
    public void AddTodo_WithNoIdsDoesNotCreateEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            reportStore.Save(new GapReport { GeneratedUtc = DateTime.UtcNow, Items = [] });
            var controller = new TodoController(reportStore, todoStore, null!);

            var result = controller.AddTodo(null!);

            Assert.Equal(0, result.Value);
            Assert.Empty(todoStore.Load());
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    [Fact]
    public void VerifyTodo_MarksOwnedEntryDoneAndReturnsUpdatedEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            todoStore.Add([Gap("owned", "Owned", "603")]);
            var (proxy, library) = LibraryProxy.Create([new Movie { ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } }]);
            var controller = new TodoController(reportStore, todoStore, new LibraryVerifier(library));

            var result = controller.VerifyTodo("owned").Value!;

            Assert.True(result.Owned);
            Assert.NotNull(result.Entry);
            Assert.True(result.Entry!.Done);
            Assert.NotNull(result.Entry.DoneUtc);
            Assert.Equal("603", proxy.LastQuery!.HasAnyProviderId!["Tmdb"]);
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    [Fact]
    public void VerifyTodo_UnknownIdReturnsEmptyOutcome()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            var (_, library) = LibraryProxy.Create([]);
            var controller = new TodoController(reportStore, todoStore, new LibraryVerifier(library));

            var result = controller.VerifyTodo("missing").Value!;

            Assert.False(result.Owned);
            Assert.Null(result.Entry);
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    [Fact]
    public void VerifyAllTodo_ReconcilesOwnedAndOutstandingEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            todoStore.Add([Gap("owned", "Owned", "603"), Gap("missing", "Missing", "604")]);
            var (_, library) = LibraryProxy.Create([new Movie { ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } }]);
            var controller = new TodoController(reportStore, todoStore, new LibraryVerifier(library));

            var result = controller.VerifyAllTodo().Value!;

            Assert.Equal(2, result.Checked);
            Assert.Equal(1, result.Owned);
            Assert.True(result.Items.Single(item => item.Id == "owned").Done);
            Assert.False(result.Items.Single(item => item.Id == "missing").Done);
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    private static GapItem Gap(string id, string name, string tmdbId)
        => new()
        {
            Id = id,
            Name = name,
            TargetKind = BaseItemKind.Movie,
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = tmdbId }
        };

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private class LibraryProxy : DispatchProxy
    {
        private IReadOnlyList<BaseItem> _items = [];

        public InternalItemsQuery? LastQuery { get; private set; }

        public static (LibraryProxy Control, ILibraryManager Service) Create(IReadOnlyList<BaseItem> items)
        {
            var proxy = (LibraryProxy)DispatchProxy.Create<ILibraryManager, LibraryProxy>();
            proxy._items = items;
            return (proxy, (ILibraryManager)proxy);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ILibraryManager.GetItemList))
            {
                LastQuery = (InternalItemsQuery?)args![0];
                if (LastQuery?.HasAnyProviderId is null || _items.Count == 0)
                {
                    return _items;
                }

                var requested = LastQuery.HasAnyProviderId.Values;
                return _items.Where(item => item.ProviderIds.Values.Any(requested.Contains)).ToArray();
            }

            return targetMethod?.ReturnType == typeof(void)
                ? null
                : targetMethod?.ReturnType.IsValueType == true
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null;
        }
    }
}
