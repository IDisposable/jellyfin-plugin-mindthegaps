using System;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// One unowned title as the web UI shows it on a card: a movie or series the library does not hold, whether
/// it came from a person's filmography, a title's "similar" list, or the scanned recommendations. The
/// <see cref="GapId"/> is the same stable id the gap report uses, so the Send action can rehydrate the gap
/// server-side from the same source the card came from.
/// </summary>
public sealed class MissingTitle
{
    /// <summary>
    /// Gets or sets the stable gap id (<c>filmography:movie:{tmdbId}</c> or <c>filmography:series:{tmdbId}</c>).
    /// </summary>
    public string GapId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release (or first-air) year, when known.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the release (or first-air) date, when known.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the person's credit on the title ("as Marty McFly", "Director"), on a person page.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Gets or sets why the title is suggested, on a recommendation ("Because you have Fargo").
    /// </summary>
    public string? Because { get; set; }

    /// <summary>
    /// Gets or sets whether this is a movie (<c>Movie</c>) or a series (<c>Series</c>).
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the title is on the want-to-watch list.
    /// </summary>
    public bool Wanted { get; set; }

    /// <summary>
    /// Gets or sets the TMDB id of the title.
    /// </summary>
    public int TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the poster URL, when TMDB has one.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the title is not yet released.
    /// </summary>
    public bool Upcoming { get; set; }
}
