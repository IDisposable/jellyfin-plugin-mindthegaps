using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class CreatorLinksTests
{
    [Fact]
    public void Person_Tmdb_LinksToPersonPage()
    {
        var links = CreatorLinks.Build("Person", new Dictionary<string, string> { ["Tmdb"] = "287" });
        var link = Assert.Single(links);
        Assert.Equal("TMDB", link.Name);
        Assert.Equal("https://www.themoviedb.org/person/287", link.Url);
    }

    [Fact]
    public void Person_ImdbNameId_LinksToNamePage()
    {
        var links = CreatorLinks.Build("Person", new Dictionary<string, string> { ["Imdb"] = "nm0000138" });
        Assert.Equal("https://www.imdb.com/name/nm0000138/", Assert.Single(links).Url);
    }

    [Fact]
    public void Imdb_TitleId_IsNotACreator()
    {
        // A "tt..." id is a title, not a person, so no name link is built.
        var links = CreatorLinks.Build("Person", new Dictionary<string, string> { ["Imdb"] = "tt0133093" });
        Assert.Empty(links);
    }

    [Fact]
    public void Author_OpenLibrary_LinksToAuthorsPage()
    {
        var links = CreatorLinks.Build("Book", new Dictionary<string, string> { ["OpenLibrary"] = "OL79034A" });
        var link = Assert.Single(links);
        Assert.Equal("OpenLibrary", link.Name);
        Assert.Equal("https://openlibrary.org/authors/OL79034A", link.Url);
    }

    [Fact]
    public void Artist_MusicBrainz_LinksToArtistPage()
    {
        var links = CreatorLinks.Build("MusicArtist", new Dictionary<string, string> { ["MusicBrainzArtist"] = "abc-123" });
        Assert.Equal("https://musicbrainz.org/artist/abc-123", Assert.Single(links).Url);
    }

    [Fact]
    public void Artist_Discogs_LinksToArtistPage()
    {
        var links = CreatorLinks.Build("MusicArtist", new Dictionary<string, string> { ["Discogs"] = "999" });
        Assert.Equal("https://www.discogs.com/artist/999", Assert.Single(links).Url);
    }

    [Fact]
    public void Label_Discogs_LinksToLabelPage()
    {
        var links = CreatorLinks.Build("MusicLabel", new Dictionary<string, string> { ["Discogs"] = "157" });
        Assert.Equal("https://www.discogs.com/label/157", Assert.Single(links).Url);
    }

    [Fact]
    public void BoxSet_Tmdb_LinksToCollectionPage()
    {
        var links = CreatorLinks.Build("BoxSet", new Dictionary<string, string> { ["Tmdb"] = "10" });
        Assert.Equal("https://www.themoviedb.org/collection/10", Assert.Single(links).Url);
    }

    [Fact]
    public void Studio_Tmdb_LinksToCompanyPage()
    {
        var links = CreatorLinks.Build("Studio", new Dictionary<string, string> { ["Tmdb"] = "41077" });
        Assert.Equal("https://www.themoviedb.org/company/41077", Assert.Single(links).Url);
    }

    [Fact]
    public void Keyword_Tmdb_LinksToKeywordPage()
    {
        var links = CreatorLinks.Build("Keyword", new Dictionary<string, string> { ["Tmdb"] = "9715" });
        Assert.Equal("https://www.themoviedb.org/keyword/9715", Assert.Single(links).Url);
    }

    [Fact]
    public void Trakt_LinksToPeoplePage()
    {
        var links = CreatorLinks.Build("Person", new Dictionary<string, string> { ["Trakt"] = "robert-zemeckis" });
        Assert.Equal("https://trakt.tv/people/robert-zemeckis", Assert.Single(links).Url);
    }

    [Fact]
    public void NullOrEmpty_IsEmpty()
    {
        Assert.Empty(CreatorLinks.Build("Person", null));
        Assert.Empty(CreatorLinks.Build("Person", new Dictionary<string, string>()));
        Assert.Empty(CreatorLinks.Build("Person", new Dictionary<string, string> { ["Tmdb"] = string.Empty }));
    }

    [Fact]
    public void UnknownSourceTypeForTmdb_IsSkipped()
    {
        // A TMDB id with no person/collection/company/keyword/title meaning produces no link.
        var links = CreatorLinks.Build("Episode", new Dictionary<string, string> { ["Tmdb"] = "123" });
        Assert.Empty(links);
    }
}
