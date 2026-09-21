using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;
using MediaBrowser.Controller.Entities;
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
}
