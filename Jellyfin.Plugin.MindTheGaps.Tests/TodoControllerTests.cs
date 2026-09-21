using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Api;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class TodoControllerTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid Viewer = Guid.NewGuid();

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
            var controller = Controller(reportStore, todoStore);

            var result = controller.AddTodo(new[] { "known", "forged", "known" });

            Assert.Equal(1, result.Value);
            var entry = Assert.Single(todoStore.Load(Admin));
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
            var controller = Controller(reportStore, todoStore);

            var result = controller.AddTodo(null!);

            Assert.Equal(0, result.Value);
            Assert.Empty(todoStore.Load(Admin));
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
            todoStore.Add(Admin, [Gap("owned", "Owned", "603")]);
            var (proxy, library) = LibraryProxy.Create([new Movie { ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } }]);
            var controller = Controller(reportStore, todoStore, new LibraryVerifier(library));

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
            var controller = Controller(reportStore, todoStore, new LibraryVerifier(library));

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
            todoStore.Add(Admin, [Gap("owned", "Owned", "603"), Gap("missing", "Missing", "604")]);
            var (_, library) = LibraryProxy.Create([new Movie { ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } }]);
            var controller = Controller(reportStore, todoStore, new LibraryVerifier(library));

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

    [Fact]
    public void AddTodo_GoesOnTheCallersOwnList()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            reportStore.Save(new GapReport { GeneratedUtc = DateTime.UtcNow, TotalGaps = 1, Items = [Gap("known", "Known", "1")] });

            Assert.Equal(1, Controller(reportStore, todoStore, user: Admin).AddTodo(["known"]).Value);

            Assert.Single(todoStore.Load(Admin));
            Assert.Empty(todoStore.Load(Viewer));
            Assert.Empty(Controller(reportStore, todoStore, user: Viewer).GetTodo().Value!.Items);
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    [Fact]
    public void ARequestThatIsNotAUsersHasNoList()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            var controller = Controller(reportStore, todoStore);
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Administrator")], "ApiKey"));

            Assert.IsType<ForbidResult>(controller.GetTodo().Result);
            Assert.IsType<ForbidResult>(controller.AddTodo(["known"]).Result);
            Assert.IsType<ForbidResult>(controller.RemoveTodo("known").Result);
            Assert.IsType<ForbidResult>(controller.SetTodoDone("known", true));
            Assert.IsType<ForbidResult>(controller.VerifyTodo("known").Result);
            Assert.IsType<ForbidResult>(controller.VerifyAllTodo().Result);
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    [Fact]
    public void TheFirstAdministratorToLoadTheListTakesTheOldServerWideOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            Directory.CreateDirectory(root + "-todo");
            File.WriteAllText(
                Path.Combine(root + "-todo", "todos.json"),
                "{\"old\":{\"id\":\"old\",\"name\":\"Old\",\"targetKindName\":\"Movie\",\"domainName\":\"Movies\",\"addedUtc\":\"2026-01-01T00:00:00Z\"}}");

            var list = Controller(reportStore, todoStore, user: Admin).GetTodo().Value!;

            Assert.Equal("old", Assert.Single(list.Items).Id);
            Assert.False(File.Exists(Path.Combine(root + "-todo", "todos.json")));
            Assert.Empty(Controller(reportStore, todoStore, user: Viewer, administrator: false).GetTodo().Value!.Items);
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    [Fact]
    public void ANonAdministratorDoesNotTakeTheOldServerWideList()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            Directory.CreateDirectory(root + "-todo");
            var legacy = Path.Combine(root + "-todo", "todos.json");
            File.WriteAllText(legacy, "{\"old\":{\"id\":\"old\",\"name\":\"Old\"}}");

            Assert.Empty(Controller(reportStore, todoStore, user: Viewer, administrator: false).GetTodo().Value!.Items);

            Assert.True(File.Exists(legacy));
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    [Fact]
    public void TheUsersIdComesFromTheServersClaim()
    {
        var id = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TodoOwner.UserIdClaim, id.ToString())], "test"));

        Assert.True(TodoOwner.TryGetUserId(principal, out var read));
        Assert.Equal(id, read);
        Assert.Equal("Jellyfin-UserId", TodoOwner.UserIdClaim);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void ARequestWithoutAUsableUserIdHasNoOwner(string? claim)
    {
        var claims = claim is null ? [] : new[] { new Claim(TodoOwner.UserIdClaim, claim) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        Assert.False(TodoOwner.TryGetUserId(principal, out _));
        Assert.False(TodoOwner.TryGetUserId(null, out _));
    }

    [Fact]
    public async System.Threading.Tasks.Task DeletingAUserDeletesTheirList()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-owner-" + Guid.NewGuid().ToString("N"));
        try
        {
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root);
            todoStore.Add(Viewer, [Gap("known", "Known", "1")]);
            todoStore.Add(Admin, [Gap("known", "Known", "1")]);
            var owner = new TodoOwner(todoStore, UserProxy.Create(Admin), NullLogger<TodoOwner>.Instance);

            await owner.OnEvent(new Jellyfin.Data.Events.Users.UserDeletedEventArgs(new Jellyfin.Database.Implementations.Entities.User("viewer", "auth", "reset") { Id = Viewer }));

            Assert.Empty(todoStore.Load(Viewer));
            Assert.Single(todoStore.Load(Admin));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheFirstUseSweepsTheListsOfUsersThatAreGone()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-owner-" + Guid.NewGuid().ToString("N"));
        try
        {
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root);
            var gone = Guid.NewGuid();
            todoStore.Add(gone, [Gap("known", "Known", "1")]);
            todoStore.Add(Admin, [Gap("known", "Known", "1")]);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TodoOwner.UserIdClaim, Admin.ToString())], "test"));

            new TodoOwner(todoStore, UserProxy.Create(Admin), NullLogger<TodoOwner>.Instance).Resolve(principal, isAdministrator: false);

            Assert.Empty(todoStore.Load(gone));
            Assert.Single(todoStore.Load(Admin));
        }
        finally
        {
            Delete(root);
        }
    }

    private static TodoController Controller(GapStore report, TodoStore todo, LibraryVerifier? verifier = null, Guid? user = null, bool administrator = true)
    {
        var id = user ?? Admin;
        var owner = new TodoOwner(todo, UserProxy.Create(new Dictionary<Guid, string> { [Admin] = "Ann", [Viewer] = "Vic" }), NullLogger<TodoOwner>.Instance);
        var claims = new List<Claim> { new(TodoOwner.UserIdClaim, id.ToString()) };
        if (administrator)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
        }

        return new TodoController(report, todo, owner, verifier!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
            }
        };
    }

    [Fact]
    public void GetEveryonesTodo_ListsEveryUsersListWithItsOwner_TheCallersFirst()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            todoStore.Add(Viewer, [Gap("v:1", "Viewer One", "1"), Gap("v:2", "Viewer Two", "2")]);
            todoStore.SetDone(Viewer, "v:2", true);
            todoStore.Add(Admin, [Gap("a:1", "Admin One", "3")]);

            var everyone = Controller(reportStore, todoStore, user: Admin).GetEveryonesTodo().Value!;

            Assert.Equal(Admin, everyone.CallerId);
            Assert.Equal(new[] { "Ann", "Vic" }, everyone.Owners.Select(o => o.UserName));
            var viewer = everyone.Owners.Single(o => o.UserId == Viewer);
            Assert.Equal(2, viewer.Count);
            Assert.Equal(1, viewer.Open);
            Assert.Equal(3, everyone.Items.Count);
            Assert.All(everyone.Items.Where(i => i.OwnerId == Viewer), i => Assert.Equal("Vic", i.OwnerName));
            Assert.Equal("Admin One", everyone.Items.Single(i => i.OwnerId == Admin).Name);
            Assert.True(everyone.Items.Single(i => i.Id == "v:2").Done);
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    [Fact]
    public void GetEveryonesTodo_ListsTheCallerEvenWithAnEmptyList_AndSkipsUsersThatAreGone()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            todoStore.Add(Guid.NewGuid(), [Gap("gone:1", "Gone", "9")]);

            var everyone = Controller(reportStore, todoStore, user: Admin).GetEveryonesTodo().Value!;

            var only = Assert.Single(everyone.Owners);
            Assert.Equal(Admin, only.UserId);
            Assert.Equal(0, only.Count);
            Assert.Empty(everyone.Items);
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    [Fact]
    public void AnAdministratorCanManageAnotherUsersList()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            todoStore.Add(Viewer, [Gap("v:1", "One", "1"), Gap("v:2", "Two", "2")]);
            todoStore.Add(Admin, [Gap("v:1", "One", "1")]);
            var controller = Controller(reportStore, todoStore, user: Admin);

            Assert.IsType<NoContentResult>(controller.SetTodoDone("v:1", true, Viewer));
            Assert.Equal(1, controller.RemoveTodo("v:2", Viewer).Value);

            var theirs = Assert.Single(todoStore.Load(Viewer));
            Assert.True(theirs.Done);
            Assert.False(Assert.Single(todoStore.Load(Admin)).Done);
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    [Fact]
    public void VerifyingAnotherUsersListChecksTheirEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            todoStore.Add(Viewer, [Gap("owned", "Owned", "603"), Gap("missing", "Missing", "604")]);
            var (_, library) = LibraryProxy.Create([new Movie { ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } }]);
            var controller = Controller(reportStore, todoStore, new LibraryVerifier(library), user: Admin);

            var result = controller.VerifyAllTodo(Viewer).Value!;

            Assert.Equal(2, result.Checked);
            Assert.Equal(1, result.Owned);
            Assert.True(todoStore.Load(Viewer).Single(e => e.Id == "owned").Done);
            Assert.True(controller.VerifyTodo("owned", Viewer).Value!.Owned);
        }
        finally
        {
            Delete(root + "-report");
            Delete(root + "-todo");
        }
    }

    [Fact]
    public void NamingAUserThatDoesNotExistIsNotFound_AndCreatesNoList()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtg-todo-controller-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reportStore = new GapStore(NullLogger<GapStore>.Instance, root + "-report");
            var todoStore = new TodoStore(NullLogger<TodoStore>.Instance, root + "-todo");
            var controller = Controller(reportStore, todoStore, user: Admin);
            var stranger = Guid.NewGuid();

            Assert.IsType<NotFoundResult>(controller.RemoveTodo("x", stranger).Result);
            Assert.IsType<NotFoundResult>(controller.SetTodoDone("x", true, stranger));
            Assert.IsType<NotFoundResult>(controller.VerifyTodo("x", stranger).Result);
            Assert.IsType<NotFoundResult>(controller.VerifyAllTodo(stranger).Result);
            Assert.False(Directory.Exists(Path.Combine(root + "-todo", "watchlists")));
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

    private class UserProxy : DispatchProxy
    {
        private Dictionary<Guid, string> _known = [];

        public static IUserManager Create(params Guid[] known)
            => Create(known.ToDictionary(id => id, _ => "user"));

        public static IUserManager Create(Dictionary<Guid, string> known)
        {
            var proxy = (UserProxy)DispatchProxy.Create<IUserManager, UserProxy>();
            proxy._known = known;
            return (IUserManager)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IUserManager.GetUserById) && args![0] is Guid id && _known.TryGetValue(id, out var name))
            {
                return new Jellyfin.Database.Implementations.Entities.User(name, "auth", "reset") { Id = id };
            }

            return targetMethod?.ReturnType.IsValueType == true && targetMethod.ReturnType != typeof(void)
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }
}
