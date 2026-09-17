using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class LibraryVerifierTests
{
    [Fact]
    public void Owns_WhenProviderIdMatches_ReturnsTrue()
    {
        var (manager, library) = LibraryManagerProxy.Create([new Movie()]);
        var verifier = new LibraryVerifier(library);

        var result = verifier.Owns(
            BaseItemKind.Movie,
            new Dictionary<string, string> { ["Tmdb"] = "123" },
            null,
            "The Title");

        Assert.True(result);
        Assert.Equal("123", manager.LastQuery!.HasAnyProviderId!["Tmdb"]);
        Assert.False(manager.LastQuery.IsVirtualItem);
    }

    [Fact]
    public void Owns_WhenOnlyAlbumArtistAndTitleMatch_ReturnsTrue()
    {
        var album = new MusicAlbum
        {
            Name = "Album",
            AlbumArtists = ["Artist"]
        };
        var (manager, library) = LibraryManagerProxy.Create([album]);
        var verifier = new LibraryVerifier(library);

        var result = verifier.Owns(
            BaseItemKind.MusicAlbum,
            new Dictionary<string, string>(),
            " artist ",
            "ALBUM");

        Assert.True(result);
        Assert.Equal("ALBUM", manager.LastQuery!.Name);
        Assert.Equal(BaseItemKind.MusicAlbum, manager.LastQuery.IncludeItemTypes[0]);
    }

    [Fact]
    public void Owns_DoesNotUseNameFallbackForMovies()
    {
        var (manager, library) = LibraryManagerProxy.Create([new Movie()]);
        var verifier = new LibraryVerifier(library);

        var result = verifier.Owns(
            BaseItemKind.Movie,
            new Dictionary<string, string>(),
            "Artist",
            "Movie");

        Assert.False(result);
        Assert.Null(manager.LastQuery);
    }

    [Fact]
    public void OwnedAmong_LargeBatchUsesOneIndexReadAndPreservesOrder()
    {
        var owned = new Movie { ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "owned" } };
        var (manager, library) = LibraryManagerProxy.Create([owned]);
        var verifier = new LibraryVerifier(library);
        var gaps = new List<GapItem>();
        for (var index = 0; index < 25; index++)
        {
            gaps.Add(new GapItem
            {
                Id = index.ToString(CultureInfo.InvariantCulture),
                Name = "Title " + index,
                TargetKind = BaseItemKind.Movie,
                ProviderIds = new Dictionary<string, string>
                {
                    ["Tmdb"] = index == 12 ? "owned" : index.ToString(CultureInfo.InvariantCulture)
                }
            });
        }

        var result = verifier.OwnedAmong(gaps);

        var match = Assert.Single(result);
        Assert.Equal("12", match.Id);
        Assert.Equal(1, manager.QueryCount);
        Assert.Equal(BaseItemKind.Movie, manager.LastQuery!.IncludeItemTypes[0]);
    }

    private class LibraryManagerProxy : DispatchProxy
    {
        public IReadOnlyList<BaseItem> Items { get; private set; } = [];

        public InternalItemsQuery? LastQuery { get; private set; }

        public int QueryCount { get; private set; }

        public static (LibraryManagerProxy Control, ILibraryManager Service) Create(IReadOnlyList<BaseItem> items)
        {
            var proxy = (LibraryManagerProxy)DispatchProxy.Create<ILibraryManager, LibraryManagerProxy>();
            proxy.Items = items;
            return (proxy, (ILibraryManager)proxy);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ILibraryManager.GetItemList))
            {
                LastQuery = (InternalItemsQuery?)args![0];
                QueryCount++;
                return Items;
            }

            return targetMethod?.ReturnType == typeof(void)
                ? null
                : targetMethod?.ReturnType.IsValueType == true
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null;
        }
    }
}
