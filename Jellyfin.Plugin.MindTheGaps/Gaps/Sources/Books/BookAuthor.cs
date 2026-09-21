using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Books;

/// <summary>
/// Which person a book's bibliography is read for: the one an owned book names as its author.
/// </summary>
internal static class BookAuthor
{
    /// <summary>
    /// Picks the author from a book's people. Books in Jellyfin carry their author as a person of type Author;
    /// the first listed person stands in when no role is set.
    /// </summary>
    /// <param name="people">The book's people, in library order.</param>
    /// <returns>The author's name, or <see langword="null"/> when the book lists nobody.</returns>
    public static string? Resolve(IEnumerable<PersonInfo> people)
    {
        ArgumentNullException.ThrowIfNull(people);

        string? firstPerson = null;
        foreach (var person in people)
        {
            if (string.IsNullOrEmpty(person.Name))
            {
                continue;
            }

            firstPerson ??= person.Name;
            if (person.Type == PersonKind.Author
                || string.Equals(person.Role, "Author", StringComparison.OrdinalIgnoreCase))
            {
                return person.Name;
            }
        }

        return firstPerson;
    }
}
