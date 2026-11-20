using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// A list read end to end: its own "ls" id (a watchlist has one too, even when addressed by user id), its
/// name, what it holds, and every entry on it.
/// </summary>
/// <param name="Id">The list's "ls" id.</param>
/// <param name="Name">The list's display name, or null.</param>
/// <param name="ListType">What the list holds: "TITLES", "PEOPLE", or "IMAGES".</param>
/// <param name="ListClass">"LIST" or "WATCH_LIST".</param>
/// <param name="Entries">The entries, in list order.</param>
internal sealed record ImdbListContents(
    string Id,
    string? Name,
    string? ListType,
    string? ListClass,
    IReadOnlyList<ImdbListEntry> Entries)
{
    /// <summary>The list type IMDb reports for a list of titles.</summary>
    public const string TitlesType = "TITLES";

    /// <summary>The list type IMDb reports for a list of people.</summary>
    public const string PeopleType = "PEOPLE";

    /// <summary>The list class IMDb reports for a watchlist.</summary>
    public const string WatchlistClass = "WATCH_LIST";

    /// <summary>
    /// Gets a value indicating whether the list is a watchlist, however it was addressed.
    /// </summary>
    public bool IsWatchlist => string.Equals(ListClass, WatchlistClass, System.StringComparison.Ordinal);

    /// <summary>
    /// Gets the title entries, dropping anything else the list holds.
    /// </summary>
    public IEnumerable<ImdbListEntry> Titles => Entries.Where(e => e.IsTitle);

    /// <summary>
    /// Gets the person entries, dropping anything else the list holds.
    /// </summary>
    public IEnumerable<ImdbListEntry> Names => Entries.Where(e => e.IsName);
}
