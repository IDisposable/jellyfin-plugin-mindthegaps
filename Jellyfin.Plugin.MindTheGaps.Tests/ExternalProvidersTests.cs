using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.MindTheGaps.Providers;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// The OpenLibrary and Discogs ids drive this plugin's own sources and core ships no provider for them, so the
// plugin registers one, and steps aside for any other plugin that provides the same key.
public class ExternalProvidersTests
{
    private static readonly ProviderPrecedence _nobodyElse = new(PluginManagerProxy.Create(), NullLogger<ProviderPrecedence>.Instance);

    [Theory]
    [InlineData("OL45804W", "https://openlibrary.org/works/OL45804W")]
    [InlineData("OL7353617M", "https://openlibrary.org/books/OL7353617M")]
    [InlineData("OL23919A", "https://openlibrary.org/authors/OL23919A")]
    public void OpenLibraryUrl_FollowsWhatTheKeyNames(string key, string expected)
        => Assert.Equal(expected, ProviderUrls.OpenLibrary(key));

    [Theory]
    [InlineData("OL45804W", true)]
    [InlineData("ol45804w", true)]
    [InlineData("OL45804", false)]
    [InlineData("45804W", false)]
    [InlineData("OLW", false)]
    [InlineData("OL4x804W", false)]
    public void IsOpenLibraryKey_AcceptsOnlyTheKeyShape(string key, bool expected)
        => Assert.Equal(expected, ProviderUrls.IsOpenLibraryKey(key));

    [Fact]
    public void OpenLibraryUrlProvider_LinksABookAndAnAuthor()
    {
        var provider = new OpenLibraryExternalUrlProvider(_nobodyElse);
        var book = new Book();
        book.SetProviderId(ProviderIds.OpenLibrary, "OL45804W");
        var author = new Person();
        author.SetProviderId(ProviderIds.OpenLibrary, "OL23919A");

        Assert.Equal("https://openlibrary.org/works/OL45804W", Assert.Single(provider.GetExternalUrls(book)));
        Assert.Equal("https://openlibrary.org/authors/OL23919A", Assert.Single(provider.GetExternalUrls(author)));
        Assert.Equal("OpenLibrary", provider.Name);
    }

    [Fact]
    public void OpenLibraryUrlProvider_SkipsAStrayValueAndOtherKinds()
    {
        var provider = new OpenLibraryExternalUrlProvider(_nobodyElse);
        var typo = new Book();
        typo.SetProviderId(ProviderIds.OpenLibrary, "not a key");
        var movie = new Movie();
        movie.SetProviderId(ProviderIds.OpenLibrary, "OL45804W");

        Assert.Empty(provider.GetExternalUrls(typo));
        Assert.Empty(provider.GetExternalUrls(movie));
    }

    [Fact]
    public void DiscogsUrlProvider_LinksAReleaseAndAnArtist()
    {
        var provider = new DiscogsExternalUrlProvider(_nobodyElse);
        var album = new MusicAlbum();
        album.SetProviderId(ProviderIds.Discogs, "249504");
        var artist = new MusicArtist();
        artist.SetProviderId(ProviderIds.Discogs, "45");
        var stray = new MusicAlbum();
        stray.SetProviderId(ProviderIds.Discogs, "r249504");

        Assert.Equal("https://www.discogs.com/release/249504", Assert.Single(provider.GetExternalUrls(album)));
        Assert.Equal("https://www.discogs.com/artist/45", Assert.Single(provider.GetExternalUrls(artist)));
        Assert.Empty(provider.GetExternalUrls(stray));
    }

    [Fact]
    public void ExternalIds_ShowOnTheKindsTheSourcesRead()
    {
        var openLibrary = new OpenLibraryExternalId(_nobodyElse);
        var discogs = new DiscogsExternalId(_nobodyElse);

        Assert.Equal("OpenLibrary", openLibrary.Key);
        Assert.Equal(ExternalIdMediaType.Book, openLibrary.Type);
        Assert.True(openLibrary.Supports(new Book()));
        Assert.False(openLibrary.Supports(new Movie()));

        Assert.Equal("Discogs", discogs.Key);
        Assert.True(discogs.Supports(new MusicAlbum()));
        Assert.True(discogs.Supports(new MusicArtist()));
        Assert.False(discogs.Supports(new Book()));
    }

    [Fact]
    public void Claims_ReadsAParameterlessProvidersKeyExactly()
    {
        var claimed = ProviderPrecedence.Claims(
            [("Some Book Plugin", [typeof(OtherOpenLibraryId), typeof(OtherOpenLibraryUrls), typeof(string)])],
            OwnProviders.Keys);

        Assert.Contains("OpenLibrary", claimed);
        Assert.DoesNotContain("Discogs", claimed);
    }

    [Fact]
    public void Claims_FallsBackToThePluginNameForAProviderItCannotCreate()
    {
        var claimed = ProviderPrecedence.Claims(
            [("Discogs Metadata", [typeof(InjectedUrlProvider)])],
            OwnProviders.Keys);

        Assert.Contains("Discogs", claimed);
        Assert.DoesNotContain("OpenLibrary", claimed);
    }

    [Fact]
    public void Claims_IgnoresAPluginWithNoProviders()
    {
        // A plugin named for a service but shipping no id or url provider of its own does not displace ours.
        var claimed = ProviderPrecedence.Claims([("Discogs Importer", [typeof(string)])], OwnProviders.Keys);

        Assert.Empty(claimed);
    }

    [Fact]
    public void Precedence_WithNoOtherPlugins_ClaimsNothing()
    {
        Assert.False(_nobodyElse.ClaimedElsewhere(ProviderIds.OpenLibrary));
        Assert.False(_nobodyElse.ClaimedElsewhere(ProviderIds.Discogs));
    }

    public sealed class OtherOpenLibraryId : IExternalId
    {
        public string ProviderName => "Open Library";

        public string Key => "OpenLibrary";

        public ExternalIdMediaType? Type => ExternalIdMediaType.Book;

        public bool Supports(IHasProviderIds item) => item is Book;
    }

    public sealed class OtherOpenLibraryUrls : IExternalUrlProvider
    {
        public string Name => "OpenLibrary";

        public IEnumerable<string> GetExternalUrls(BaseItem item) => [];
    }

    public sealed class InjectedUrlProvider : IExternalUrlProvider
    {
        public InjectedUrlProvider(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);
        }

        public string Name => "Discogs";

        public IEnumerable<string> GetExternalUrls(BaseItem item) => [];
    }

    private class PluginManagerProxy : DispatchProxy
    {
        public static IPluginManager Create() => DispatchProxy.Create<IPluginManager, PluginManagerProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name == "get_Plugins"
                ? Array.Empty<LocalPlugin>()
                : targetMethod?.ReturnType.IsValueType == true ? Activator.CreateInstance(targetMethod.ReturnType) : null;
    }
}
