using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class BookAuthorTests
{
    [Fact]
    public void Resolve_PicksThePersonTypedAuthor()
    {
        var people = new[]
        {
            new PersonInfo { Name = "An Illustrator", Type = PersonKind.Illustrator },
            new PersonInfo { Name = "An Author", Type = PersonKind.Author }
        };

        Assert.Equal("An Author", BookAuthor.Resolve(people));
    }

    [Fact]
    public void Resolve_AcceptsAnAuthorRoleOnAnotherType()
    {
        var people = new[]
        {
            new PersonInfo { Name = "Someone Else", Type = PersonKind.Actor },
            new PersonInfo { Name = "The Writer", Type = PersonKind.Unknown, Role = "author" }
        };

        Assert.Equal("The Writer", BookAuthor.Resolve(people));
    }

    [Fact]
    public void Resolve_FallsBackToTheFirstNamedPerson()
    {
        var people = new[]
        {
            new PersonInfo { Name = string.Empty, Type = PersonKind.Unknown },
            new PersonInfo { Name = "First Listed", Type = PersonKind.Unknown },
            new PersonInfo { Name = "Second Listed", Type = PersonKind.Unknown }
        };

        Assert.Equal("First Listed", BookAuthor.Resolve(people));
    }

    [Fact]
    public void Resolve_IsNullWhenNobodyIsListed()
    {
        Assert.Null(BookAuthor.Resolve([]));
    }

    [Theory]
    [InlineData("OL23919A", "OL23919A")]
    [InlineData(" ol23919a ", "OL23919A")]
    [InlineData("OL45804W", null)]
    [InlineData("not a key", null)]
    public void OpenLibraryKey_AcceptsOnlyAnAuthorKey(string stored, string? expected)
    {
        var person = new Person();
        person.SetProviderId(ProviderIds.OpenLibrary, stored);

        Assert.Equal(expected, BookAuthor.OpenLibraryKey(person));
    }

    [Fact]
    public void OpenLibraryKey_IsNullForNoPersonOrNoKey()
    {
        Assert.Null(BookAuthor.OpenLibraryKey(null));
        Assert.Null(BookAuthor.OpenLibraryKey(new Person()));
    }
}
