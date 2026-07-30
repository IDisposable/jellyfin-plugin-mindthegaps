using System;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Services.Imdb;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// imdb_people_list.json is a keyless capture of a public IMDb people list, the input the filmography seed
// source reads. Re-capture with the same call documented in ImdbListTests, passing {"id":"ls576275547","first":25}.
//
// The gap building itself is FilmographyGapMapper's, already covered by TmdbFilmographyCapturedDataTests, so
// what is pinned here is the part this source adds: reading Name entries off a list, and keying the creator
// on the IMDb name id rather than a library guid.
public class ImdbPeopleListTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void APeopleListYieldsNameEntriesAndNoTitles()
    {
        var list = Load();

        Assert.Equal(ImdbListContents.PeopleType, list.ListType);
        Assert.Equal(23, list.Names.Count());
        Assert.Empty(list.Titles);
        Assert.All(list.Names, n => Assert.StartsWith("nm", n.Id, StringComparison.Ordinal));
        Assert.Contains(list.Names, n => n.NameText?.Value == "Steven Spielberg");
    }

    [Fact]
    public void ANameEntryCarriesNoTitleFields()
    {
        // Both fragments are spread onto the same union, so a Name entry deserializes with the Title fields
        // null. IsTitle/IsName is what keeps each source off the other's entries.
        var spielberg = Load().Names.Single(n => n.NameText?.Value == "Steven Spielberg");

        Assert.True(spielberg.IsName);
        Assert.False(spielberg.IsTitle);
        Assert.Null(spielberg.ReleaseYear);
        Assert.Null(spielberg.TitleType);
        Assert.Equal("nm0000229", spielberg.Id);
        Assert.NotNull(spielberg.PrimaryImage?.Url);
    }

    [Fact]
    public void TheCreatorIsKeyedOnTheImdbNameIdNotTheList()
    {
        // The same person named on two lists has to group once, and the id has to be stable across scans
        // because a whole-creator dismissal is stored against it.
        Assert.Equal("imdbperson-nm0000229", SourceItemIds.ImdbPerson("nm0000229"));
    }

    private static ImdbListContents Load()
    {
        var response = JsonSerializer.Deserialize<ImdbGraphResponse>(
            TestData.Read("imdb_people_list.json"),
            _jsonOptions);
        var list = response?.Data?.List;
        Assert.NotNull(list);

        var entries = (list!.Items?.Edges ?? [])
            .Select(e => e.Node?.Item)
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
        return new ImdbListContents(list.Id!, list.Name?.Value, list.ListType?.Id, entries);
    }
}
