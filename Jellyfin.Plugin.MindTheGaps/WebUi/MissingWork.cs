using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// One unowned work by an artist or author the library holds, as an item page shows it on a card: an album
/// or a book. Unlike a <see cref="MissingTitle"/> it carries its own links rather than a TMDB id, since these
/// works are identified by MusicBrainz, Discogs and OpenLibrary and there is no shared detail lookup to ask.
/// The <see cref="GapId"/> is the same stable id the gap report uses, so a todo add can rehydrate the gap
/// server-side.
/// </summary>
public sealed class MissingWork
{
    /// <summary>
    /// Gets or sets the stable gap id.
    /// </summary>
    public string GapId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title of the album or book.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release (or first publication) year, when known.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the release (or first publication) date, when known.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets whether this is an album (<c>MusicAlbum</c>) or a book (<c>Book</c>).
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the owned artist or author the work is by.
    /// </summary>
    public string? Creator { get; set; }

    /// <summary>
    /// Gets or sets the cover URL, when the provider has one.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the work is not yet released.
    /// </summary>
    public bool Upcoming { get; set; }

    /// <summary>
    /// Gets or sets the links to the work on the services that know it.
    /// </summary>
    public IReadOnlyList<ExternalLink> Links { get; set; } = Array.Empty<ExternalLink>();
}
