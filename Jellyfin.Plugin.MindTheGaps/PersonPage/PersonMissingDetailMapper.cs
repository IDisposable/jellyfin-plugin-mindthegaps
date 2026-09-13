using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Model;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.TvShows;

namespace Jellyfin.Plugin.MindTheGaps.PersonPage;

/// <summary>
/// The pure half of the detail view: shapes TMDB's movie or series record into what the page shows.
/// </summary>
internal static class PersonMissingDetailMapper
{
    /// <summary>
    /// Maps a movie.
    /// </summary>
    /// <param name="gap">The gap the page asked about (its id and the person's credit).</param>
    /// <param name="movie">TMDB's movie, with external ids and videos.</param>
    /// <param name="posterUrl">Resolves a poster path to a URL.</param>
    /// <param name="backdropUrl">Resolves a backdrop path to a URL.</param>
    /// <returns>The detail.</returns>
    public static PersonMissingDetail FromMovie(GapItem gap, Movie movie, Func<string?, string?> posterUrl, Func<string?, string?> backdropUrl)
    {
        ArgumentNullException.ThrowIfNull(gap);
        ArgumentNullException.ThrowIfNull(movie);

        return new PersonMissingDetail
        {
            GapId = gap.Id,
            Kind = "Movie",
            Title = string.IsNullOrEmpty(movie.Title) ? gap.Name : movie.Title,
            Year = movie.ReleaseDate?.Year ?? gap.Year,
            ReleaseDate = movie.ReleaseDate ?? gap.ReleaseDate,
            Tagline = Blank(movie.Tagline),
            Overview = Blank(movie.Overview),
            RuntimeMinutes = movie.Runtime is > 0 ? movie.Runtime : null,
            Genres = GenreNames(movie.Genres),
            Rating = movie.VoteCount > 0 ? Math.Round(movie.VoteAverage, 1) : null,
            VoteCount = movie.VoteCount,
            Status = Blank(movie.Status),
            Role = gap.Overview,
            PosterUrl = posterUrl(movie.PosterPath) ?? gap.ImageUrl,
            BackdropUrl = backdropUrl(movie.BackdropPath),
            TmdbUrl = string.Create(CultureInfo.InvariantCulture, $"https://www.themoviedb.org/movie/{movie.Id}"),
            ImdbUrl = ImdbUrl(movie.ExternalIds?.ImdbId ?? movie.ImdbId),
            TrailerUrl = TrailerUrl(movie.Videos?.Results)
        };
    }

    /// <summary>
    /// Maps a series.
    /// </summary>
    /// <param name="gap">The gap the page asked about (its id and the person's credit).</param>
    /// <param name="show">TMDB's series, with external ids and videos.</param>
    /// <param name="posterUrl">Resolves a poster path to a URL.</param>
    /// <param name="backdropUrl">Resolves a backdrop path to a URL.</param>
    /// <returns>The detail.</returns>
    public static PersonMissingDetail FromSeries(GapItem gap, TvShow show, Func<string?, string?> posterUrl, Func<string?, string?> backdropUrl)
    {
        ArgumentNullException.ThrowIfNull(gap);
        ArgumentNullException.ThrowIfNull(show);

        return new PersonMissingDetail
        {
            GapId = gap.Id,
            Kind = "Series",
            Title = string.IsNullOrEmpty(show.Name) ? gap.Name : show.Name,
            Year = show.FirstAirDate?.Year ?? gap.Year,
            ReleaseDate = show.FirstAirDate ?? gap.ReleaseDate,
            Tagline = Blank(show.Tagline),
            Overview = Blank(show.Overview),
            RuntimeMinutes = show.EpisodeRunTime?.FirstOrDefault(r => r > 0) is int runtime and > 0 ? runtime : null,
            Genres = GenreNames(show.Genres),
            Rating = show.VoteCount > 0 ? Math.Round(show.VoteAverage, 1) : null,
            VoteCount = show.VoteCount,
            Status = Blank(show.Status),
            Seasons = show.NumberOfSeasons > 0 ? show.NumberOfSeasons : null,
            Episodes = show.NumberOfEpisodes > 0 ? show.NumberOfEpisodes : null,
            Network = show.Networks?.Select(n => n.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
            Role = gap.Overview,
            PosterUrl = posterUrl(show.PosterPath) ?? gap.ImageUrl,
            BackdropUrl = backdropUrl(show.BackdropPath),
            TmdbUrl = string.Create(CultureInfo.InvariantCulture, $"https://www.themoviedb.org/tv/{show.Id}"),
            ImdbUrl = ImdbUrl(show.ExternalIds?.ImdbId),
            TrailerUrl = TrailerUrl(show.Videos?.Results)
        };
    }

    /// <summary>
    /// Picks the trailer to link: an official YouTube trailer first, then any YouTube trailer, then any
    /// YouTube teaser. Only YouTube, since that is the one host the link can be built for without an API.
    /// </summary>
    /// <param name="videos">TMDB's videos for the title.</param>
    /// <returns>The URL, or <see langword="null"/>.</returns>
    public static string? TrailerUrl(IEnumerable<Video>? videos)
    {
        if (videos is null)
        {
            return null;
        }

        Video? best = null;
        var bestScore = -1;
        foreach (var video in videos)
        {
            if (string.IsNullOrEmpty(video.Key) || !string.Equals(video.Site, "YouTube", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Type outranks provenance: any trailer beats any teaser, and "official" only breaks ties.
            var score = string.Equals(video.Type, "Trailer", StringComparison.OrdinalIgnoreCase) ? 10
                : string.Equals(video.Type, "Teaser", StringComparison.OrdinalIgnoreCase) ? 1
                : -1;
            if (score < 0)
            {
                continue;
            }

            if (video.Official)
            {
                score += 1;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = video;
            }
        }

        return best?.Key is null ? null : "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(best.Key);
    }

    private static string? ImdbUrl(string? imdbId)
        => string.IsNullOrWhiteSpace(imdbId) ? null : "https://www.imdb.com/title/" + Uri.EscapeDataString(imdbId.Trim()) + "/";

    private static IReadOnlyList<string> GenreNames(IEnumerable<Genre>? genres)
        => genres is null ? [] : genres.Select(g => g.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
