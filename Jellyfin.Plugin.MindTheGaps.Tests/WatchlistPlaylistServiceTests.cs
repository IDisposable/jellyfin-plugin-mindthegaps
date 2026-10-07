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
using MediaBrowser.Controller.Entities.TV;
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
        foreach (var item in ownedItems)
        {
            control.Items[item.Id] = item;
        }

        var service = new WatchlistPlaylistService(
            playlists,
            verifier,
            NullLogger<WatchlistPlaylistService>.Instance,
            playlist => playlist.LinkedChildren.Select(c => c.ItemId is { } id ? control.Items.GetValueOrDefault(id) : null).OfType<BaseItem>(),
            entry => entry is Episode episode ? control.Items.GetValueOrDefault(episode.SeriesId) : entry,
            (_, _) => true);
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

    private static readonly PluginConfiguration On = new() { WantToWatchPlaylistEnabled = true, WantToWatchRowIncludesOwned = true };

    private static Movie AMovie(string name = "A Movie") => new() { Id = Guid.NewGuid(), Name = name };

    // A series and its episodes, as the library holds them: the playlist manager stores the episodes.
    private static (Series Series, Episode[] Episodes) ASeries(string name, int episodes)
    {
        var series = new Series { Id = Guid.NewGuid(), Name = name };
        var eps = Enumerable.Range(1, episodes)
            .Select(n => new Episode { Id = Guid.NewGuid(), Name = name + " " + n, SeriesId = series.Id })
            .ToArray();
        return (series, eps);
    }

    private static Jellyfin.Database.Implementations.Entities.User TheUser()
        => new("u", "auth", "reset") { Id = User };

    [Fact]
    public async Task RemoveTitleAsync_TakesAMovieOffTheUsersOwnPlaylist_ByItsIdInTheNForm()
    {
        var movie = AMovie();
        var (playlists, service) = Build([movie, AMovie("Other")]);
        var playlist = PlaylistWith(User, "Want to Watch", playlists.Items.Keys.ToArray());
        playlists.Existing.Add(playlist);

        Assert.True(await service.RemoveTitleAsync(User, movie.Id, On));

        Assert.Equal(playlist.Id.ToString("N"), playlists.LastRemovedPlaylistId);
        Assert.Equal([movie.Id.ToString("N")], playlists.LastRemovedEntryIds);
    }

    [Fact]
    public async Task RemoveTitleAsync_DoesNothing_ForATitleNotOnThePlaylist_OrWithThePlaylistOff_OrNoPlaylist()
    {
        var movie = AMovie();
        var (playlists, service) = Build([movie, AMovie("Other")]);

        Assert.False(await service.RemoveTitleAsync(User, movie.Id, On));

        playlists.Existing.Add(PlaylistWith(User, "Want to Watch", playlists.Items.Keys.First(id => id != movie.Id)));
        Assert.False(await service.RemoveTitleAsync(User, movie.Id, On));

        playlists.Existing.Clear();
        playlists.Existing.Add(PlaylistWith(User, "Want to Watch", movie.Id));
        Assert.False(await service.RemoveTitleAsync(User, movie.Id, new PluginConfiguration { WantToWatchPlaylistEnabled = false }));
        Assert.False(await service.RemoveTitleAsync(User, movie.Id, null));

        Assert.Null(playlists.LastRemovedEntryIds);
    }

    [Fact]
    public async Task RemoveTitleAsync_NeverTouchesAnotherUsersPlaylistSharedUnderTheSameName()
    {
        var movie = AMovie();
        var (playlists, service) = Build([movie]);
        playlists.Shared.Add(PlaylistWith(Guid.NewGuid(), "Want to Watch", movie.Id));

        Assert.False(await service.RemoveTitleAsync(User, movie.Id, On));
        Assert.Null(playlists.LastRemovedEntryIds);
    }

    [Fact]
    public async Task AddArrivedAsync_CreatesItsOwnPlaylist_RatherThanAddingToAnotherUsersSharedOne()
    {
        var movie = new Movie { Id = Guid.NewGuid(), ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } };
        var (playlists, service) = Build([movie]);
        var shared = PlaylistWith(Guid.NewGuid(), "Want to Watch");
        playlists.Shared.Add(shared);

        await service.AddArrivedAsync(User, [Entry("Movie", "603")], On, CancellationToken.None);

        Assert.Single(playlists.CreatedRequests);
        Assert.NotEqual(shared.Id, playlists.LastAddedPlaylistId);
    }

    [Fact]
    public async Task AddItemAsync_CreatesThePlaylistTheFirstTime_AndAddsTheItem()
    {
        var movie = AMovie();
        var (playlists, service) = Build([movie]);

        Assert.True(await service.AddItemAsync(User, movie.Id, On));

        Assert.Equal("Want to Watch", Assert.Single(playlists.CreatedRequests).Name);
        Assert.Equal([movie.Id], playlists.LastAddedItemIds);
        Assert.Equal(User, playlists.LastAddedUserId);
        Assert.True(service.Contains(User, movie.Id, On));
    }

    [Fact]
    public async Task AddItemAsync_AddsToTheExistingPlaylist_AndNotTwice()
    {
        var movie = AMovie();
        var other = AMovie("Other");
        var (playlists, service) = Build([movie, other]);
        var playlist = PlaylistWith(User, "Want to Watch", movie.Id);
        playlists.Existing.Add(playlist);

        Assert.False(await service.AddItemAsync(User, movie.Id, On));
        Assert.Null(playlists.LastAddedItemIds);

        Assert.True(await service.AddItemAsync(User, other.Id, On));
        Assert.Equal(playlist.Id, playlists.LastAddedPlaylistId);
        Assert.Equal([other.Id], playlists.LastAddedItemIds);
        Assert.Empty(playlists.CreatedRequests);
    }

    [Fact]
    public async Task AddItemAsync_DoesNothing_WithThePlaylistOff()
    {
        var movie = AMovie();
        var (playlists, service) = Build([movie]);

        Assert.False(await service.AddItemAsync(User, movie.Id, new PluginConfiguration { WantToWatchPlaylistEnabled = false }));
        Assert.False(await service.AddItemAsync(User, movie.Id, null));
        Assert.Empty(playlists.CreatedRequests);
        Assert.Null(playlists.LastAddedItemIds);
    }

    [Fact]
    public async Task ConcurrentFirstAdds_CreateOnePlaylist_AndAddEachTitleOnce()
    {
        var a = AMovie("A");
        var b = AMovie("B");
        var (playlists, service) = Build([a, b]);
        playlists.CreateDelay = TimeSpan.FromMilliseconds(50);

        await Task.WhenAll(
            service.AddItemAsync(User, a.Id, On),
            service.AddItemAsync(User, b.Id, On),
            service.AddItemAsync(User, a.Id, On));

        var playlist = Assert.Single(playlists.Existing);
        Assert.Single(playlists.CreatedRequests);
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x), playlist.LinkedChildren.Select(c => c.ItemId!.Value).OrderBy(x => x));
    }

    [Fact]
    public async Task ASeries_IsStoredAsItsEpisodes_AndStillCountsAsOnThePlaylist_InTheRow_AndForRemoval()
    {
        var (series, episodes) = ASeries("A Show", 3);
        var (playlists, service) = Build([series, .. episodes]);

        Assert.True(await service.AddItemAsync(User, series.Id, On));

        var playlist = Assert.Single(playlists.Existing);
        Assert.Equal(episodes.Select(e => e.Id), playlist.LinkedChildren.Select(c => c.ItemId!.Value));
        Assert.True(service.Contains(User, series.Id, On));
        Assert.False(await service.AddItemAsync(User, series.Id, On));
        Assert.Equal([series.Id], service.GetOwned(TheUser(), On).Select(t => t.Id).Where(id => id == series.Id));

        Assert.True(await service.RemoveTitleAsync(User, series.Id, On));
        Assert.Equal(episodes.Select(e => e.Id.ToString("N")), playlists.LastRemovedEntryIds);
        Assert.Empty(playlist.LinkedChildren);
        Assert.False(service.Contains(User, series.Id, On));
    }

    [Fact]
    public async Task RemoveEntryAsync_TakesOffOnlyTheWatchedEpisode()
    {
        var (series, episodes) = ASeries("A Show", 2);
        var (playlists, service) = Build([series, .. episodes]);
        playlists.Existing.Add(PlaylistWith(User, "Want to Watch", episodes.Select(e => e.Id).ToArray()));

        Assert.True(await service.RemoveEntryAsync(User, episodes[0].Id, On));
        Assert.Equal([episodes[0].Id.ToString("N")], playlists.LastRemovedEntryIds);
        Assert.True(service.Contains(User, series.Id, On));

        Assert.False(await service.RemoveEntryAsync(User, series.Id, On));
    }

    [Fact]
    public void Contains_IsTrueOnlyForATitleOnTheUsersOwnPlaylist_WithThePlaylistOn()
    {
        var mine = AMovie("Mine");
        var sharedItem = AMovie("Shared");
        var (playlists, service) = Build([mine, sharedItem]);
        playlists.Existing.Add(PlaylistWith(User, "Want to Watch", mine.Id));
        playlists.Shared.Add(PlaylistWith(Guid.NewGuid(), "Want to Watch", sharedItem.Id));

        Assert.True(service.Contains(User, mine.Id, On));
        Assert.False(service.Contains(User, sharedItem.Id, On));
        Assert.False(service.Contains(User, Guid.NewGuid(), On));
        Assert.False(service.Contains(User, mine.Id, new PluginConfiguration { WantToWatchPlaylistEnabled = false }));
    }

    [Fact]
    public async Task AddArrivedAsync_DoesNotAddATitleAlreadyOnThePlaylist()
    {
        var movieId = Guid.NewGuid();
        var movie = new Movie { Id = movieId, ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" } };
        var (playlists, service) = Build([movie]);
        playlists.Existing.Add(PlaylistWith(User, "Want to Watch", movieId));

        await service.AddArrivedAsync(User, [Entry("Movie", "603")], On, CancellationToken.None);

        Assert.Null(playlists.LastAddedItemIds);
        Assert.Empty(playlists.CreatedRequests);
    }

    [Fact]
    public void IsWantable_IsARealMovieOrSeries()
    {
        Assert.True(WatchlistPlaylistService.IsWantable(new Movie()));
        Assert.True(WatchlistPlaylistService.IsWantable(new Series()));
        Assert.False(WatchlistPlaylistService.IsWantable(new Movie { IsVirtualItem = true }));
        Assert.False(WatchlistPlaylistService.IsWantable(new Series { IsVirtualItem = true }));
        Assert.False(WatchlistPlaylistService.IsWantable(new Episode()));
        Assert.False(WatchlistPlaylistService.IsWantable(null));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void GetOwned_IsEmpty_UnlessBothThePlaylistAndTheRowOptionAreOn(bool playlist, bool includeOwned)
    {
        var movie = AMovie();
        var (playlists, service) = Build([movie]);
        playlists.Existing.Add(PlaylistWith(User, "Want to Watch", movie.Id));
        var config = new PluginConfiguration { WantToWatchPlaylistEnabled = playlist, WantToWatchRowIncludesOwned = includeOwned };

        Assert.Empty(service.GetOwned(TheUser(), config));
        Assert.Empty(service.GetOwned(TheUser(), null));
    }

    [Fact]
    public void OwnedTitles_AreTheVisibleRealTitles_OnceEach_NewestAddedFirst()
    {
        var (show, eps) = ASeries("Show", 2);
        var first = AMovie("First");
        var hidden = AMovie("Hidden");
        var minted = new Movie { Id = Guid.NewGuid(), Name = "Minted", IsVirtualItem = true };
        var last = AMovie("Last");
        var titles = new Dictionary<Guid, BaseItem> { [show.Id] = show };
        Func<BaseItem, BaseItem?> titleOf = e => e is Episode ep ? titles.GetValueOrDefault(ep.SeriesId) : e;

        var owned = WatchlistPlaylistService.OwnedTitles([first, eps[0], null, hidden, minted, eps[1], last], titleOf, item => item != hidden);

        Assert.Equal(["Last", "Show", "First"], owned.Select(i => i.Name));
    }

    [Fact]
    public void EntriesOf_IsTheMovieItself_OrTheSeriesEpisodes()
    {
        var (show, eps) = ASeries("Show", 2);
        var movie = AMovie();

        Assert.Equal([movie.Id], WatchlistPlaylistService.EntriesOf([movie, eps[0], eps[1]], movie.Id));
        Assert.Equal(eps.Select(e => e.Id), WatchlistPlaylistService.EntriesOf([movie, eps[0], eps[1]], show.Id));
        Assert.Empty(WatchlistPlaylistService.EntriesOf([movie], show.Id));
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
    // A fake playlist host: it stores what is added and removed on the playlist objects themselves, and, as
    // Jellyfin's Playlist.GetPlaylistItems does, adds a series as its episodes rather than as itself.
    private class PlaylistManagerProxy : DispatchProxy
    {
        public Dictionary<Guid, BaseItem> Items { get; } = [];

        public List<Playlist> Existing { get; } = [];

        // Other users' playlists shared with the caller: GetPlaylists returns these too, as the host does.
        public List<Playlist> Shared { get; } = [];

        public TimeSpan CreateDelay { get; set; } = TimeSpan.Zero;

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

        private Playlist ById(string id) => Existing.Concat(Shared).Single(p => p.Id.ToString("N") == id || p.Id.ToString() == id);

        private async Task<PlaylistCreationResult> CreateAsync(PlaylistCreationRequest request)
        {
            CreatedRequests.Add(request);
            if (CreateDelay > TimeSpan.Zero)
            {
                await Task.Delay(CreateDelay);
            }

            var created = new Playlist { Id = Guid.NewGuid(), Name = request.Name, OwnerUserId = request.UserId, LinkedChildren = [] };
            Existing.Add(created);
            return new PlaylistCreationResult(created.Id.ToString());
        }

        private IEnumerable<Guid> Expand(Guid id)
            => Items.GetValueOrDefault(id) is Series series
                ? Items.Values.OfType<Episode>().Where(e => e.SeriesId == series.Id).Select(e => e.Id)
                : [id];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IPlaylistManager.GetPlaylists):
                    return Existing.Where(p => p.OwnerUserId == (Guid)args![0]!).Concat(Shared).ToArray().AsEnumerable();
                case nameof(IPlaylistManager.RemoveItemFromPlaylistAsync):
                    LastRemovedPlaylistId = (string)args![0]!;
                    LastRemovedEntryIds = ((IEnumerable<string>)args[1]!).ToList();
                    var from = ById(LastRemovedPlaylistId);
                    from.LinkedChildren = from.LinkedChildren.Where(c => !LastRemovedEntryIds.Contains(c.ItemId!.Value.ToString("N"))).ToArray();
                    return Task.CompletedTask;
                case nameof(IPlaylistManager.CreatePlaylist):
                    return CreateAsync((PlaylistCreationRequest)args![0]!);
                case nameof(IPlaylistManager.AddItemToPlaylistAsync):
                    LastAddedPlaylistId = (Guid)args![0]!;
                    LastAddedItemIds = ((IEnumerable<Guid>)args[1]!).ToList();
                    LastAddedUserId = (Guid)args[^1]!;
                    var to = Existing.Concat(Shared).Single(p => p.Id == LastAddedPlaylistId);
                    to.LinkedChildren = to.LinkedChildren
                        .Concat(LastAddedItemIds.SelectMany(Expand).Select(id => new LinkedChild { ItemId = id }))
                        .ToArray();
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
