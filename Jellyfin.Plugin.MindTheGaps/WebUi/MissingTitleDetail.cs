using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// What the person page shows about one unowned title before it is sent for download: TMDB's own summary
/// of the movie or series, plus the links that let the viewer read more.
/// </summary>
public sealed class MissingTitleDetail
{
    /// <summary>
    /// Gets or sets the gap id the page asked about.
    /// </summary>
    public string GapId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this is a movie (<c>Movie</c>) or a series (<c>Series</c>).
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release (or first-air) year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the release (or first-air) date.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the tagline.
    /// </summary>
    public string? Tagline { get; set; }

    /// <summary>
    /// Gets or sets the overview.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// Gets or sets the runtime in minutes (a movie's runtime, a series' typical episode runtime).
    /// </summary>
    public int? RuntimeMinutes { get; set; }

    /// <summary>
    /// Gets or sets the genre names.
    /// </summary>
    public IReadOnlyList<string> Genres { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets TMDB's average rating out of 10.
    /// </summary>
    public double? Rating { get; set; }

    /// <summary>
    /// Gets or sets the number of TMDB votes behind the rating.
    /// </summary>
    public int VoteCount { get; set; }

    /// <summary>
    /// Gets or sets TMDB's status ("Released", "Post Production", "Returning Series", "Ended").
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the number of seasons (series only).
    /// </summary>
    public int? Seasons { get; set; }

    /// <summary>
    /// Gets or sets the number of episodes (series only).
    /// </summary>
    public int? Episodes { get; set; }

    /// <summary>
    /// Gets or sets the network (series only).
    /// </summary>
    public string? Network { get; set; }

    /// <summary>
    /// Gets or sets the person's credit on the title, as the card showed it (person page only).
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Gets or sets why the title is suggested ("Because you have Fargo"), on a recommendation.
    /// </summary>
    public string? Because { get; set; }

    /// <summary>
    /// Gets or sets the poster URL.
    /// </summary>
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Gets or sets the backdrop URL.
    /// </summary>
    public string? BackdropUrl { get; set; }

    /// <summary>
    /// Gets or sets the TMDB page URL.
    /// </summary>
    public string? TmdbUrl { get; set; }

    /// <summary>
    /// Gets or sets the IMDb page URL, when TMDB knows the IMDb id.
    /// </summary>
    public string? ImdbUrl { get; set; }

    /// <summary>
    /// Gets or sets a YouTube trailer URL, when TMDB lists one.
    /// </summary>
    public string? TrailerUrl { get; set; }
}
