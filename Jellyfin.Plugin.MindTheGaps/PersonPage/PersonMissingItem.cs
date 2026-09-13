using System;

namespace Jellyfin.Plugin.MindTheGaps.PersonPage;

/// <summary>
/// One unowned title on a person's page: a movie or series the person is credited on that the library does
/// not hold. The <see cref="GapId"/> is the same stable id the gap report uses, so the Send action can
/// rehydrate the gap server-side.
/// </summary>
public sealed class PersonMissingItem
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
    /// Gets or sets the person's credit on the title: "as Marty McFly", "Director", "Screenplay".
    /// </summary>
    public string? Role { get; set; }

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
