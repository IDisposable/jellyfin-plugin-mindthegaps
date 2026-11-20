using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

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
    public static string? Resolve(IEnumerable<PersonInfo> people) => ResolvePerson(people)?.Name;

    /// <summary>
    /// Picks the author from a book's people, as <see cref="Resolve"/> does.
    /// </summary>
    /// <param name="people">The book's people, in library order.</param>
    /// <returns>The author, or <see langword="null"/> when the book lists nobody.</returns>
    public static PersonInfo? ResolvePerson(IEnumerable<PersonInfo> people)
    {
        ArgumentNullException.ThrowIfNull(people);

        PersonInfo? firstPerson = null;
        foreach (var person in people)
        {
            if (string.IsNullOrEmpty(person.Name))
            {
                continue;
            }

            firstPerson ??= person;
            if (person.Type == PersonKind.Author
                || string.Equals(person.Role, "Author", StringComparison.OrdinalIgnoreCase))
            {
                return person;
            }
        }

        return firstPerson;
    }

    /// <summary>
    /// The OpenLibrary author key set on the author's own person item, which is how a wrong namesake is fixed
    /// by hand.
    /// </summary>
    /// <param name="author">The author's person item, or null.</param>
    /// <returns>The author key ("OL23919A"), or <see langword="null"/> when the person carries none.</returns>
    public static string? OpenLibraryKey(IHasProviderIds? author)
    {
        var key = author?.GetProviderId(ProviderIds.OpenLibrary)?.Trim().ToUpperInvariant();
        return ProviderUrls.IsOpenLibraryKey(key) && key![^1] == 'A' ? key : null;
    }
}
