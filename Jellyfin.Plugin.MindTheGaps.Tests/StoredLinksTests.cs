using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// A report and a list keep ids, not links: the links are built from the ids whenever they are read, an older
// file's links give up the ids they alone carried, and a known image or offer host is stored as a token.
public sealed class StoredLinksTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mtg-stored-links-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Theory]
    [InlineData("https://image.tmdb.org/t/p/w342/abc.jpg", "~tmdbimg/w342/abc.jpg")]
    [InlineData("https://www.themoviedb.org/movie/603/watch?locale=US", "~tmdb/movie/603/watch?locale=US")]
    [InlineData("https://covers.openlibrary.org/b/id/123-M.jpg", "~olcover/123-M.jpg")]
    [InlineData("https://coverartarchive.org/release-group/abc/front-250", "~caa/abc/front-250")]
    [InlineData("https://example.com/poster.jpg", "https://example.com/poster.jpg")]
    public void StoredUrls_RoundTrip(string url, string stored)
    {
        Assert.Equal(stored, StoredUrls.Compact(url));
        Assert.Equal(url, StoredUrls.Expand(stored));
    }

    [Fact]
    public void StoredUrls_LeavesAnUnknownTokenAlone()
    {
        Assert.Equal("~nothing/abc", StoredUrls.Expand("~nothing/abc"));
        Assert.Null(StoredUrls.Expand(null));
    }

    [Fact]
    public void Recover_TurnsAStoredJustWatchPageIntoItsId()
    {
        var gap = new GapItem
        {
            ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" },
            Links = [new ExternalLink("JustWatch", "https://www.justwatch.com/us/movie/the-matrix")]
        };

        LegacyLinkIds.Recover(gap);

        Assert.Equal("/us/movie/the-matrix", gap.ProviderIds[ProviderIds.JustWatch]);
        Assert.Equal("603", gap.ProviderIds["Tmdb"]);
    }

    [Fact]
    public void Recover_IgnoresASearchPageAndKeepsAnIdAlreadyThere()
    {
        var search = new GapItem { Links = [new ExternalLink("JustWatch", "https://www.justwatch.com/us/search?q=matrix")] };
        var kept = new GapItem
        {
            ProviderIds = new Dictionary<string, string> { ["JustWatch"] = "/us/movie/kept" },
            Links = [new ExternalLink("JustWatch", "https://www.justwatch.com/us/movie/other")]
        };

        LegacyLinkIds.Recover(search);
        LegacyLinkIds.Recover(kept);

        Assert.False(search.ProviderIds.ContainsKey(ProviderIds.JustWatch));
        Assert.Equal("/us/movie/kept", kept.ProviderIds["JustWatch"]);
    }

    [Theory]
    [InlineData("Person", "https://www.themoviedb.org/person/31-tom-hanks", "Tmdb", "31")]
    [InlineData("Studio", "https://www.themoviedb.org/company/41077", "Tmdb", "41077")]
    [InlineData("Person", "https://www.imdb.com/name/nm0000158/", "Imdb", "nm0000158")]
    [InlineData("Person", "https://trakt.tv/people/tom-hanks", "Trakt", "tom-hanks")]
    [InlineData("MusicArtist", "https://musicbrainz.org/artist/a74b1b7f-71a5-4011-9441-d0b5e4122711", "MusicBrainzArtist", "a74b1b7f-71a5-4011-9441-d0b5e4122711")]
    [InlineData("Book", "https://openlibrary.org/authors/OL23919A", "OpenLibrary", "OL23919A")]
    [InlineData("MusicLabel", "https://www.discogs.com/label/1866", "Discogs", "1866")]
    public void Recover_ReadsASourcesIdsBackOutOfItsLinks(string type, string url, string key, string id)
    {
        var gap = new GapItem { SourceItemType = type, SourceLinks = [new ExternalLink("x", url)] };

        LegacyLinkIds.Recover(gap);

        Assert.Equal(id, gap.SourceProviderIds![key]);

        // The ids rebuild the same page the stored link held.
        Assert.Contains(CreatorLinks.Build(type, gap.SourceProviderIds), l => url.StartsWith(l.Url, StringComparison.Ordinal) || l.Url == url);
    }

    [Fact]
    public void Store_WritesNoLinksAndTokensForKnownHosts_ThenRebuildsThemOnLoad()
    {
        var gap = Gap();
        new GapStore(NullLogger<GapStore>.Instance, _dir).Save(Report(gap));

        var json = File.ReadAllText(Path.Combine(_dir, "gaps-movies.json"));
        Assert.DoesNotContain("\"links\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sourceLinks\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("image.tmdb.org", json, StringComparison.Ordinal);
        Assert.Contains("~tmdbimg/w342/abc.jpg", json, StringComparison.Ordinal);
        Assert.Contains("~tmdb/movie/603/watch", json, StringComparison.Ordinal);

        var loaded = Assert.Single(new GapStore(NullLogger<GapStore>.Instance, _dir).Load().Items);
        Assert.Equal("https://image.tmdb.org/t/p/w342/abc.jpg", loaded.ImageUrl);
        Assert.Equal("https://www.themoviedb.org/movie/603/watch?locale=US", loaded.Availability[0].Url);
        Assert.Contains(loaded.Links, l => l.Url == "https://www.themoviedb.org/movie/603");
        Assert.Contains(loaded.Links, l => l.Url == "https://www.justwatch.com/us/movie/the-matrix");
        Assert.Contains(loaded.SourceLinks, l => l.Url == "https://www.themoviedb.org/collection/2344");
    }

    [Fact]
    public void Store_ConvertsAnOlderFileOnLoad()
    {
        // As an earlier version wrote it: full addresses, stored links, and ids only inside those links.
        var old = Gap();
        old.ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" };
        old.SourceProviderIds = null;
        old.Links = [new ExternalLink("TMDB", "https://www.themoviedb.org/movie/603"), new ExternalLink("JustWatch", "https://www.justwatch.com/us/movie/the-matrix")];
        old.SourceLinks = [new ExternalLink("TMDB", "https://www.themoviedb.org/company/41077")];
        old.SourceItemType = "Studio";
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "gaps-meta.json"), JsonSerializer.Serialize(new GapReport { GeneratedUtc = DateTime.UtcNow, TotalGaps = 1 }, web));
        File.WriteAllText(Path.Combine(_dir, "gaps-movies.json"), JsonSerializer.Serialize(new[] { old }, web));

        var store = new GapStore(NullLogger<GapStore>.Instance, _dir);
        var loaded = Assert.Single(store.Load().Items);

        Assert.Equal("/us/movie/the-matrix", loaded.ProviderIds["JustWatch"]);
        Assert.Equal("41077", loaded.SourceProviderIds!["Tmdb"]);
        Assert.Contains(loaded.Links, l => l.Name == "JustWatch" && l.Url == "https://www.justwatch.com/us/movie/the-matrix");
        Assert.Contains(loaded.SourceLinks, l => l.Url == "https://www.themoviedb.org/company/41077");

        // The next save writes the converted shape.
        store.Save(store.Load());
        var json = File.ReadAllText(Path.Combine(_dir, "gaps-movies.json"));
        Assert.DoesNotContain("\"links\"", json, StringComparison.Ordinal);
        Assert.Contains("\"JustWatch\": \"/us/movie/the-matrix\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TodoList_KeepsIdsAndRebuildsLinksWhenRead()
    {
        var userId = Guid.NewGuid();
        new TodoStore(NullLogger<TodoStore>.Instance, _dir).Add(userId, [Gap()]);

        var file = Directory.GetFiles(Path.Combine(_dir, "watchlists"), "*.json").Single();
        var json = File.ReadAllText(file);
        Assert.DoesNotContain("\"links\"", json, StringComparison.Ordinal);
        Assert.Contains("~tmdbimg/", json, StringComparison.Ordinal);

        var entry = Assert.Single(new TodoStore(NullLogger<TodoStore>.Instance, _dir).Load(userId));
        Assert.Equal("https://image.tmdb.org/t/p/w342/abc.jpg", entry.ImageUrl);
        Assert.Contains(entry.Links, l => l.Url == "https://www.themoviedb.org/movie/603");
        Assert.Contains(entry.Links, l => l.Url == "https://www.justwatch.com/us/movie/the-matrix");
    }

    [Fact]
    public void ProviderLinks_AddsTraktFromTheImdbIdOnlyWhenAsked()
    {
        var ids = new Dictionary<string, string> { ["Imdb"] = "tt0133093" };

        Assert.DoesNotContain(ProviderLinks.Build(BaseItemKind.Movie, ids), l => l.Name == "Trakt");
        Assert.Contains(ProviderLinks.Build(BaseItemKind.Movie, ids, trakt: true), l => l.Url == "https://trakt.tv/movies/tt0133093");
        Assert.Contains(ProviderLinks.Build(BaseItemKind.Series, ids, trakt: true), l => l.Url == "https://trakt.tv/shows/tt0133093");
        Assert.DoesNotContain(ProviderLinks.Build(BaseItemKind.Episode, ids, trakt: true), l => l.Name == "Trakt");
    }

    private static GapItem Gap() => GapItemFactory.Create(
        id: "collection:603",
        pattern: GapPattern.SetCompletion,
        domain: MediaDomain.Movies,
        targetKind: BaseItemKind.Movie,
        name: "The Matrix",
        providerIds: new Dictionary<string, string> { ["Tmdb"] = "603", ["JustWatch"] = "/us/movie/the-matrix" },
        sourceItemId: "0123456789abcdef0123456789abcdef",
        sourceItemName: "The Matrix Collection",
        sourceItemType: "BoxSet",
        sourceProviderIds: new Dictionary<string, string> { ["Tmdb"] = "2344" },
        imageUrl: "https://image.tmdb.org/t/p/w342/abc.jpg") is var gap
            ? WithOffer(gap)
            : throw new InvalidOperationException();

    private static GapItem WithOffer(GapItem gap)
    {
        gap.Availability = [new AvailabilityOffer { Provider = "Netflix", Url = "https://www.themoviedb.org/movie/603/watch?locale=US" }];
        return gap;
    }

    private static GapReport Report(GapItem gap) => new() { GeneratedUtc = DateTime.UtcNow, TotalGaps = 1, Items = [gap] };
}
