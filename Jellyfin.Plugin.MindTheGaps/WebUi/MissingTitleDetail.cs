using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// TMDB's own record of an unowned title, for the detail dialog: enough to decide whether it is worth
/// acquiring before sending it anywhere.
/// </summary>
public sealed class MissingTitleDetail
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this is a movie (<c>Movie</c>) or a series (<c>Series</c>).
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TMDB id.
    /// </summary>
    public int TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the release (or first-air) year, when known.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the tagline, when TMDB has one.
    /// </summary>
    public string? Tagline { get; set; }

    /// <summary>
    /// Gets or sets the overview/synopsis.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// Gets or sets the genre names.
    /// </summary>
    public IReadOnlyList<string> Genres { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the runtime in minutes (a movie, or a series' typical episode length). Null when TMDB
    /// does not report one.
    /// </summary>
    public int? RuntimeMinutes { get; set; }

    /// <summary>
    /// Gets or sets TMDB's average user rating (0 to 10), or null when it has too few votes to be meaningful.
    /// </summary>
    public double? VoteAverage { get; set; }

    /// <summary>
    /// Gets or sets TMDB's status for the title (for example "Released", "Returning Series", "Ended").
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the number of seasons, for a series.
    /// </summary>
    public int? NumberOfSeasons { get; set; }

    /// <summary>
    /// Gets or sets the originating network names, for a series.
    /// </summary>
    public IReadOnlyList<string> Networks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the poster URL, when TMDB has one.
    /// </summary>
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Gets or sets the backdrop URL, when TMDB has one.
    /// </summary>
    public string? BackdropUrl { get; set; }

    /// <summary>
    /// Gets or sets the themoviedb.org page for this title.
    /// </summary>
    public string TmdbUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the imdb.com page, when the title carries an IMDb id.
    /// </summary>
    public string? ImdbUrl { get; set; }

    /// <summary>
    /// Gets or sets a JustWatch search for this title in the configured region, so a viewer can see where it
    /// streams. A search, not a title page: TMDB carries no JustWatch id.
    /// </summary>
    public string? JustWatchUrl { get; set; }

    /// <summary>
    /// Gets or sets a YouTube video id for the most relevant trailer (a trailer over a teaser, official
    /// breaking a tie within the same type), or null when TMDB has no usable video.
    /// </summary>
    public string? YoutubeTrailerKey { get; set; }
}
