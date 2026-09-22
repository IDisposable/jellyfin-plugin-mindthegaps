using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Todo;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// No live HTTP client behind this source (TodoStore is a local, in-memory-cached read), so unlike the other
// discovery sources it is fully deterministic and safe to run end to end rather than only through its mapper.
public class EveryoneWatchlistGapSourceTests
{
    private static readonly Guid Ann = Guid.NewGuid();
    private static readonly Guid Vic = Guid.NewGuid();

    [Fact]
    public void IsEnabled_FollowsItsOwnToggleOnly()
    {
        var todo = TodoStoreWith(out var root);
        try
        {
            var source = new EveryoneWatchlistGapSource(todo, Owner(todo));

            Assert.False(source.IsEnabled(new PluginConfiguration { ScanEveryoneWatchlist = false }));
            Assert.True(source.IsEnabled(new PluginConfiguration { ScanEveryoneWatchlist = true }));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Wiring_DeclaresItsKindPrefixAndOwnedKinds()
    {
        var todo = TodoStoreWith(out var root);
        try
        {
            var source = new EveryoneWatchlistGapSource(todo, Owner(todo));

            Assert.Equal(SourceItemTypes.EveryoneWatchlist, source.DiscoverKind);
            Assert.Equal("everyonewatchlist:", source.GapIdPrefix);
            Assert.Equal(
                new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.MusicAlbum, BaseItemKind.Book },
                source.OwnedKinds);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task FindGapsAsync_FoldsEveryExistingUsersOpenEntriesIntoOneGap()
    {
        var todo = TodoStoreWith(out var root);
        try
        {
            todo.Add(Ann, [Gap("v:1", "Mad Max 2", "76341")]);
            todo.Add(Vic, [Gap("v:2", "Mad Max 2", "76341")]);
            var stranger = Guid.NewGuid();
            todo.Add(stranger, [Gap("v:3", "Gone User's Pick", "9")]);
            var source = new EveryoneWatchlistGapSource(todo, Owner(todo, Ann, Vic));
            var context = new GapScanContext(new PluginConfiguration(), new OwnershipIndex(new HashSet<string>(StringComparer.Ordinal)));

            var gaps = await Collect(source, context);

            var gap = Assert.Single(gaps);
            Assert.Equal("Mad Max 2", gap.Name);
            Assert.Equal("Wanted by Ann, Vic", gap.Overview);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task FindGapsAsync_SkipsATitleTheLibraryAlreadyOwns()
    {
        var todo = TodoStoreWith(out var root);
        try
        {
            todo.Add(Ann, [Gap("v:1", "Mad Max 2", "76341")]);
            var source = new EveryoneWatchlistGapSource(todo, Owner(todo, Ann));
            var owned = new HashSet<string>(StringComparer.Ordinal) { OwnershipIndex.MakeKey(BaseItemKind.Movie, "Tmdb", "76341") };
            var context = new GapScanContext(new PluginConfiguration(), new OwnershipIndex(owned));

            Assert.Empty(await Collect(source, context));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void StillInScope_IsFalseWhenTheToggleIsOff()
    {
        var todo = TodoStoreWith(out var root);
        try
        {
            todo.Add(Ann, [Gap("v:1", "Mad Max 2", "76341")]);
            var source = new EveryoneWatchlistGapSource(todo, Owner(todo, Ann));
            var item = new GapItem { Id = "everyonewatchlist:v:1" };

            Assert.False(source.StillInScope(item, new PluginConfiguration { ScanEveryoneWatchlist = false }));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void StillInScope_IsFalseOnceTheEntryIsRemovedOrMarkedDone()
    {
        var todo = TodoStoreWith(out var root);
        try
        {
            todo.Add(Ann, [Gap("v:1", "Mad Max 2", "76341")]);
            var source = new EveryoneWatchlistGapSource(todo, Owner(todo, Ann));
            var config = new PluginConfiguration { ScanEveryoneWatchlist = true };
            var item = new GapItem { Id = "everyonewatchlist:v:1" };

            Assert.True(source.StillInScope(item, config));

            todo.SetDone(Ann, "v:1", true);

            Assert.False(source.StillInScope(item, config));
        }
        finally
        {
            Delete(root);
        }
    }

    private static async System.Threading.Tasks.Task<List<GapItem>> Collect(EveryoneWatchlistGapSource source, GapScanContext context)
    {
        var gaps = new List<GapItem>();
        await foreach (var gap in source.FindGapsAsync(context, CancellationToken.None))
        {
            gaps.Add(gap);
        }

        return gaps;
    }

    private static GapItem Gap(string id, string name, string tmdbId)
        => new()
        {
            Id = id,
            Name = name,
            TargetKind = BaseItemKind.Movie,
            Domain = MediaDomain.Movies,
            Pattern = GapPattern.Recommendation,
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = tmdbId }
        };

    private static TodoStore TodoStoreWith(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "mtg-everyone-watchlist-" + Guid.NewGuid().ToString("N"));
        return new TodoStore(NullLogger<TodoStore>.Instance, root);
    }

    private static TodoOwner Owner(TodoStore todo, params Guid[] known)
        => new(todo, UserProxy.Create(known.ToDictionary(id => id, id => id == Ann ? "Ann" : id == Vic ? "Vic" : "user")), NullLogger<TodoOwner>.Instance);

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private class UserProxy : DispatchProxy
    {
        private Dictionary<Guid, string> _known = [];

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
