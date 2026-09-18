using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.TvShows;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Turns TMDbLib's own movie/series detail objects into the flat record the detail dialog renders.
/// </summary>
internal static class MissingTitleDetailMapper
{
    /// <summary>
    /// Maps a movie's TMDB detail record.
    /// </summary>
    /// <param name="movie">The movie, fetched with external ids and videos.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <param name="backdropUrl">Resolves a TMDB backdrop path to a URL.</param>
    /// <returns>The detail.</returns>
    public static MissingTitleDetail FromMovie(Movie movie, Func<string?, string?> posterUrl, Func<string?, string?> backdropUrl)
    {
        ArgumentNullException.ThrowIfNull(movie);

        var imdbId = string.IsNullOrEmpty(movie.ImdbId) ? movie.ExternalIds?.ImdbId : movie.ImdbId;
        return new MissingTitleDetail
        {
            Title = movie.Title ?? string.Empty,
            Kind = "Movie",
            TmdbId = movie.Id,
            Year = movie.ReleaseDate?.Year,
            Tagline = string.IsNullOrWhiteSpace(movie.Tagline) ? null : movie.Tagline,
            Overview = movie.Overview,
            Genres = GenreNames(movie.Genres),
            RuntimeMinutes = movie.Runtime,
            VoteAverage = movie.VoteCount > 0 ? movie.VoteAverage : null,
            Status = movie.Status,
            PosterUrl = posterUrl(movie.PosterPath),
            BackdropUrl = backdropUrl(movie.BackdropPath),
            TmdbUrl = TmdbLinks.TitleUrl(BaseItemKind.Movie, movie.Id.ToString(CultureInfo.InvariantCulture)) ?? string.Empty,
            ImdbUrl = ImdbUrl(imdbId),
            YoutubeTrailerKey = SelectTrailer(movie.Videos?.Results)
        };
    }

    /// <summary>
    /// Maps a series' TMDB detail record.
    /// </summary>
    /// <param name="show">The series, fetched with external ids and videos.</param>
    /// <param name="posterUrl">Resolves a TMDB poster path to a URL.</param>
    /// <param name="backdropUrl">Resolves a TMDB backdrop path to a URL.</param>
    /// <returns>The detail.</returns>
    public static MissingTitleDetail FromSeries(TvShow show, Func<string?, string?> posterUrl, Func<string?, string?> backdropUrl)
    {
        ArgumentNullException.ThrowIfNull(show);

        var runtime = show.EpisodeRunTime?.FirstOrDefault(r => r > 0);
        return new MissingTitleDetail
        {
            Title = show.Name ?? string.Empty,
            Kind = "Series",
            TmdbId = show.Id,
            Year = show.FirstAirDate?.Year,
            Tagline = string.IsNullOrWhiteSpace(show.Tagline) ? null : show.Tagline,
            Overview = show.Overview,
            Genres = GenreNames(show.Genres),
            RuntimeMinutes = runtime is > 0 ? runtime : null,
            VoteAverage = show.VoteCount > 0 ? show.VoteAverage : null,
            Status = show.Status,
            NumberOfSeasons = show.NumberOfSeasons > 0 ? show.NumberOfSeasons : null,
            Networks = (show.Networks ?? []).Select(n => n.Name).Where(n => !string.IsNullOrEmpty(n)).ToList()!,
            PosterUrl = posterUrl(show.PosterPath),
            BackdropUrl = backdropUrl(show.BackdropPath),
            TmdbUrl = TmdbLinks.TitleUrl(BaseItemKind.Series, show.Id.ToString(CultureInfo.InvariantCulture)) ?? string.Empty,
            ImdbUrl = ImdbUrl(show.ExternalIds?.ImdbId),
            YoutubeTrailerKey = SelectTrailer(show.Videos?.Results)
        };
    }

    private static IReadOnlyList<string> GenreNames(IEnumerable<Genre>? genres)
        => (genres ?? []).Select(g => g.Name).Where(n => !string.IsNullOrEmpty(n)).ToList()!;

    private static string? ImdbUrl(string? imdbId)
        => string.IsNullOrEmpty(imdbId) ? null : string.Create(CultureInfo.InvariantCulture, $"https://www.imdb.com/title/{imdbId}/");

    // Type outranks provenance: any trailer beats any teaser, official only breaks a tie within the same
    // type. Only YouTube videos are usable (a direct video id for an embed/watch link).
    private static string? SelectTrailer(IEnumerable<Video>? videos)
    {
        var youtube = (videos ?? [])
            .Where(v => string.Equals(v.Site, "YouTube", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(v.Key))
            .ToList();
        if (youtube.Count == 0)
        {
            return null;
        }

        var best = youtube.FirstOrDefault(v => string.Equals(v.Type, "Trailer", StringComparison.OrdinalIgnoreCase) && v.Official)
            ?? youtube.FirstOrDefault(v => string.Equals(v.Type, "Trailer", StringComparison.OrdinalIgnoreCase))
            ?? youtube.FirstOrDefault(v => v.Official)
            ?? youtube[0];
        return best.Key;
    }
}
