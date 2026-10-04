using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class WatchlistPlaylistServiceTests
{
    private static readonly Guid User = Guid.NewGuid();

    private static TodoEntry Entry(string kind, string tmdbId, string name = "Some Title")
        => new()
        {
            Id = "watchlistsearch:" + kind.ToLowerInvariant() + ":" + tmdbId,
            Name = name,
            TargetKindName = kind,
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = tmdbId }
        };

    private static (PlaylistManagerProxy Playlists, WatchlistPlaylistService Service) Build(IReadOnlyList<BaseItem> ownedItems)
    {
        var (_, library) = LibraryManagerProxy.Create(ownedItems);
        var verifier = new LibraryVerifier(library);
        var (control, playlists) = PlaylistManagerProxy.Create();
        var service = new WatchlistPlaylistService(playlists, verifier, NullLogger<WatchlistPlaylistService>.Instance);
        return (control, service);
    }

    [Fact]
    public async Task AddArrivedAsync_WhenFeatureIsOff_DoesNothing()
    {
        var movie = new Movie { Id = Guid.NewGuid(), ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } };
        var (playlists, service) = Build([movie]);
        var config = new PluginConfiguration { WantToWatchPlaylistEnabled = false };

        await service.AddArrivedAsync(User, [Entry("Movie", "603")], config, CancellationToken.None);

        Assert.Empty(playlists.CreatedRequests);
        Assert.Null(playlists.LastAddedItemIds);
    }

    [Fact]
    public async Task AddArrivedAsync_WhenConfigIsNull_DoesNothing()
    {
        var movie = new Movie { Id = Guid.NewGuid(), ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } };
        var (playlists, service) = Build([movie]);

        await service.AddArrivedAsync(User, [Entry("Movie", "603")], null, CancellationToken.None);

        Assert.Empty(playlists.CreatedRequests);
    }

    [Fact]
    public async Task AddArrivedAsync_SkipsEntriesThatAreNotMoviesOrSeries()
    {
        var (playlists, service) = Build([]);
        var config = new PluginConfiguration { WantToWatchPlaylistEnabled = true };

        await service.AddArrivedAsync(User, [Entry("Book", "9")], config, CancellationToken.None);

        Assert.Empty(playlists.CreatedRequests);
        Assert.Null(playlists.LastAddedItemIds);
    }

    [Fact]
    public async Task AddArrivedAsync_SkipsAnEntryTheLibraryDoesNotActuallyHold()
    {
        // The library owns nothing, so FindOwnedItemId resolves to null for every entry: exactly what a
        // minted (virtual) placeholder also looks like, since LibraryVerifier's queries always exclude them.
        var (playlists, service) = Build([]);
        var config = new PluginConfiguration { WantToWatchPlaylistEnabled = true };

        await service.AddArrivedAsync(User, [Entry("Movie", "603")], config, CancellationToken.None);

        Assert.Empty(playlists.CreatedRequests);
        Assert.Null(playlists.LastAddedItemIds);
    }

    [Fact]
    public async Task AddArrivedAsync_CreatesThePlaylist_WhenNoneExists_AndAddsTheResolvedItem()
    {
        var movieId = Guid.NewGuid();
        var movie = new Movie { Id = movieId, ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } };
        var (playlists, service) = Build([movie]);
        var config = new PluginConfiguration { WantToWatchPlaylistEnabled = true, WantToWatchPlaylistName = "My Watchlist" };

        await service.AddArrivedAsync(User, [Entry("Movie", "603")], config, CancellationToken.None);

        var created = Assert.Single(playlists.CreatedRequests);
        Assert.Equal("My Watchlist", created.Name);
        Assert.Equal(User, created.UserId);
        Assert.Equal(User, playlists.LastAddedUserId);
        Assert.Equal([movieId], playlists.LastAddedItemIds);
    }

    [Fact]
    public async Task AddArrivedAsync_ReusesAnExistingPlaylistByName_RatherThanCreatingAnother()
    {
        var movieId = Guid.NewGuid();
        var movie = new Movie { Id = movieId, ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } };
        var (playlists, service) = Build([movie]);
        playlists.Existing.Add(new Playlist { Id = Guid.NewGuid(), Name = "My Watchlist", OwnerUserId = User });
        var config = new PluginConfiguration { WantToWatchPlaylistEnabled = true, WantToWatchPlaylistName = "My Watchlist" };

        await service.AddArrivedAsync(User, [Entry("Movie", "603")], config, CancellationToken.None);

        Assert.Empty(playlists.CreatedRequests);
        Assert.Equal(playlists.Existing[0].Id, playlists.LastAddedPlaylistId);
    }

    private static Playlist PlaylistWith(Guid owner, string name, params Guid[] itemIds)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            OwnerUserId = owner,
            LinkedChildren = itemIds.Select(id => new LinkedChild { ItemId = id }).ToArray()
        };

    [Fact]
    public async Task RemoveAsync_TakesTheItemOffTheUsersOwnPlaylist_ByItsIdInTheNForm()
    {
        var itemId = Guid.NewGuid();
        var (playlists, service) = Build([]);
        var playlist = PlaylistWith(User, "Want to Watch", Guid.NewGuid(), itemId);
        playlists.Existing.Add(playlist);
        var config = new PluginConfiguration { WantToWatchPlaylistEnabled = true };

        Assert.True(await service.RemoveAsync(User, itemId, config));

        Assert.Equal(playlist.Id.ToString("N"), playlists.LastRemovedPlaylistId);
        Assert.Equal([itemId.ToString("N")], playlists.LastRemovedEntryIds);
    }

    [Fact]
    public async Task RemoveAsync_DoesNothing_ForAnItemNotOnThePlaylist_OrWithThePlaylistOff_OrNoPlaylist()
    {
        var itemId = Guid.NewGuid();
        var (playlists, service) = Build([]);
        var on = new PluginConfiguration { WantToWatchPlaylistEnabled = true };

        Assert.False(await service.RemoveAsync(User, itemId, on));

        playlists.Existing.Add(PlaylistWith(User, "Want to Watch", Guid.NewGuid()));
        Assert.False(await service.RemoveAsync(User, itemId, on));

        playlists.Existing.Add(PlaylistWith(User, "Want to Watch", itemId));
        Assert.False(await service.RemoveAsync(User, itemId, new PluginConfiguration { WantToWatchPlaylistEnabled = false }));
        Assert.False(await service.RemoveAsync(User, itemId, null));

        Assert.Null(playlists.LastRemovedEntryIds);
    }

    [Fact]
    public async Task RemoveAsync_NeverTouchesAnotherUsersPlaylistSharedUnderTheSameName()
    {
        var itemId = Guid.NewGuid();
        var (playlists, service) = Build([]);
        playlists.Shared.Add(PlaylistWith(Guid.NewGuid(), "Want to Watch", itemId));

        Assert.False(await service.RemoveAsync(User, itemId, new PluginConfiguration { WantToWatchPlaylistEnabled = true }));
        Assert.Null(playlists.LastRemovedEntryIds);
    }

    [Fact]
    public async Task AddArrivedAsync_CreatesItsOwnPlaylist_RatherThanAddingToAnotherUsersSharedOne()
    {
        var movie = new Movie { Id = Guid.NewGuid(), ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } };
        var (playlists, service) = Build([movie]);
        var shared = PlaylistWith(Guid.NewGuid(), "Want to Watch");
        playlists.Shared.Add(shared);

        await service.AddArrivedAsync(User, [Entry("Movie", "603")], new PluginConfiguration { WantToWatchPlaylistEnabled = true }, CancellationToken.None);

        Assert.Single(playlists.CreatedRequests);
        Assert.NotEqual(shared.Id, playlists.LastAddedPlaylistId);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void GetOwned_IsEmpty_UnlessBothThePlaylistAndTheRowOptionAreOn(bool playlist, bool includeOwned)
    {
        var (playlists, service) = Build([]);
        playlists.Existing.Add(PlaylistWith(User, "Want to Watch", Guid.NewGuid()));
        var user = new Jellyfin.Database.Implementations.Entities.User("u", "auth", "reset") { Id = User };
        var config = new PluginConfiguration { WantToWatchPlaylistEnabled = playlist, WantToWatchRowIncludesOwned = includeOwned };

        Assert.Empty(service.GetOwned(user, config));
        Assert.Empty(service.GetOwned(user, null));
    }

    [Fact]
    public void OwnedTitles_AreTheVisibleMoviesAndSeries_NewestAddedFirst()
    {
        var first = new Movie { Id = Guid.NewGuid(), Name = "First" };
        var episode = new MediaBrowser.Controller.Entities.TV.Episode { Id = Guid.NewGuid(), Name = "An Episode" };
        var hidden = new Movie { Id = Guid.NewGuid(), Name = "Hidden" };
        var show = new MediaBrowser.Controller.Entities.TV.Series { Id = Guid.NewGuid(), Name = "Show" };
        var last = new Movie { Id = Guid.NewGuid(), Name = "Last" };

        var owned = WatchlistPlaylistService.OwnedTitles([first, episode, null, hidden, show, last], item => item != hidden);

        Assert.Equal(["Last", "Show", "First"], owned.Select(i => i.Name));
    }

    private class LibraryManagerProxy : DispatchProxy
    {
        private IReadOnlyList<BaseItem> _items = [];

        public static (LibraryManagerProxy Control, ILibraryManager Service) Create(IReadOnlyList<BaseItem> items)
        {
            var proxy = (LibraryManagerProxy)DispatchProxy.Create<ILibraryManager, LibraryManagerProxy>();
            proxy._items = items;
            return (proxy, (ILibraryManager)proxy);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ILibraryManager.GetItemList))
            {
                return _items;
            }

            return targetMethod?.ReturnType.IsValueType == true && targetMethod.ReturnType != typeof(void)
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }

    // Tracks calls in plain fields rather than re-deriving state from a fake host, since the test only
    // needs to see what WatchlistPlaylistService asked for.
    private class PlaylistManagerProxy : DispatchProxy
    {
        public List<Playlist> Existing { get; } = [];

        // Other users' playlists shared with the caller: GetPlaylists returns these too, as the host does.
        public List<Playlist> Shared { get; } = [];

        public string? LastRemovedPlaylistId { get; private set; }

        public IReadOnlyList<string>? LastRemovedEntryIds { get; private set; }

        public List<PlaylistCreationRequest> CreatedRequests { get; } = [];

        public Guid? LastAddedPlaylistId { get; private set; }

        public IReadOnlyList<Guid>? LastAddedItemIds { get; private set; }

        public Guid? LastAddedUserId { get; private set; }

        public static (PlaylistManagerProxy Control, IPlaylistManager Service) Create()
        {
            var proxy = (PlaylistManagerProxy)DispatchProxy.Create<IPlaylistManager, PlaylistManagerProxy>();
            return (proxy, (IPlaylistManager)proxy);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IPlaylistManager.GetPlaylists):
                    return Existing.Where(p => p.OwnerUserId == (Guid)args![0]!).Concat(Shared).ToArray().AsEnumerable();
                case nameof(IPlaylistManager.RemoveItemFromPlaylistAsync):
                    LastRemovedPlaylistId = (string)args![0]!;
                    LastRemovedEntryIds = ((IEnumerable<string>)args[1]!).ToList();
                    return Task.CompletedTask;
                case nameof(IPlaylistManager.CreatePlaylist):
                    var request = (PlaylistCreationRequest)args![0]!;
                    CreatedRequests.Add(request);
                    var created = new Playlist { Id = Guid.NewGuid(), Name = request.Name, OwnerUserId = request.UserId };
                    Existing.Add(created);
                    return Task.FromResult(new PlaylistCreationResult(created.Id.ToString()));
                case nameof(IPlaylistManager.AddItemToPlaylistAsync):
                    LastAddedPlaylistId = (Guid)args![0]!;
                    LastAddedItemIds = ((IEnumerable<Guid>)args[1]!).ToList();
                    LastAddedUserId = (Guid)args[^1]!;
                    return Task.CompletedTask;
                default:
                    return targetMethod?.ReturnType == typeof(void)
                        ? null
                        : targetMethod?.ReturnType.IsValueType == true
                            ? Activator.CreateInstance(targetMethod.ReturnType)
                            : null;
            }
        }
    }
}
