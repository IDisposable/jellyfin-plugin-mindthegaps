using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.TvShows;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class MissingTitleDetailMapperTests
{
    [Fact]
    public void FromMovie_MapsTheFullShape()
    {
        var movie = new Movie
        {
            Id = 550,
            Title = "Fight Club",
            ReleaseDate = new DateTime(1999, 10, 15, 0, 0, 0, DateTimeKind.Utc),
            Tagline = "Mischief. Mayhem. Soap.",
            Overview = "An insomniac office worker...",
            Genres = new List<Genre> { new() { Id = 18, Name = "Drama" } },
            Runtime = 139,
            VoteCount = 100,
            VoteAverage = 8.4,
            Status = "Released",
            PosterPath = "/poster.jpg",
            BackdropPath = "/backdrop.jpg",
            ImdbId = "tt0137523",
            Videos = new ResultContainer<Video>
            {
                Results = new List<Video>
                {
                    new() { Site = "YouTube", Key = "teaser1", Type = "Teaser", Official = true },
                    new() { Site = "YouTube", Key = "trailer1", Type = "Trailer", Official = false }
                }
            }
        };

        var detail = MissingTitleDetailMapper.FromMovie(movie, path => "poster:" + path, path => "backdrop:" + path);

        Assert.Equal("Fight Club", detail.Title);
        Assert.Equal("Movie", detail.Kind);
        Assert.Equal(550, detail.TmdbId);
        Assert.Equal(1999, detail.Year);
        Assert.Equal("Mischief. Mayhem. Soap.", detail.Tagline);
        Assert.Equal("An insomniac office worker...", detail.Overview);
        Assert.Equal(["Drama"], detail.Genres);
        Assert.Equal(139, detail.RuntimeMinutes);
        Assert.Equal(8.4, detail.VoteAverage);
        Assert.Equal("Released", detail.Status);
        Assert.Equal("poster:/poster.jpg", detail.PosterUrl);
        Assert.Equal("backdrop:/backdrop.jpg", detail.BackdropUrl);
        Assert.Equal("https://www.themoviedb.org/movie/550", detail.TmdbUrl);
        Assert.Equal("https://www.imdb.com/title/tt0137523/", detail.ImdbUrl);
        Assert.Equal("trailer1", detail.YoutubeTrailerKey);
    }

    [Fact]
    public void FromMovie_FallsBackToExternalIdsImdbWhenTheMovieHasNone()
    {
        var movie = new Movie
        {
            Id = 1,
            Title = "Untitled",
            ExternalIds = new ExternalIdsMovie { ImdbId = "tt9999999" }
        };

        var detail = MissingTitleDetailMapper.FromMovie(movie, _ => null, _ => null);

        Assert.Equal("https://www.imdb.com/title/tt9999999/", detail.ImdbUrl);
    }

    [Fact]
    public void FromMovie_VoteAverageIsNullWithNoVotes()
    {
        var movie = new Movie { Id = 1, Title = "Untitled", VoteCount = 0, VoteAverage = 5.0 };

        var detail = MissingTitleDetailMapper.FromMovie(movie, _ => null, _ => null);

        Assert.Null(detail.VoteAverage);
    }

    [Fact]
    public void FromMovie_TaglineIsNullWhenBlank()
    {
        var movie = new Movie { Id = 1, Title = "Untitled", Tagline = "   " };

        var detail = MissingTitleDetailMapper.FromMovie(movie, _ => null, _ => null);

        Assert.Null(detail.Tagline);
    }

    [Fact]
    public void FromMovie_NoTrailerWhenNoUsableVideoExists()
    {
        var movie = new Movie
        {
            Id = 1,
            Title = "Untitled",
            Videos = new ResultContainer<Video>
            {
                Results = new List<Video> { new() { Site = "Vimeo", Key = "v1", Type = "Trailer", Official = true } }
            }
        };

        var detail = MissingTitleDetailMapper.FromMovie(movie, _ => null, _ => null);

        Assert.Null(detail.YoutubeTrailerKey);
    }

    [Fact]
    public void FromMovie_PrefersAnOfficialTrailerOverAnUnofficialOne()
    {
        var movie = new Movie
        {
            Id = 1,
            Title = "Untitled",
            Videos = new ResultContainer<Video>
            {
                Results = new List<Video>
                {
                    new() { Site = "YouTube", Key = "unofficial", Type = "Trailer", Official = false },
                    new() { Site = "YouTube", Key = "official", Type = "Trailer", Official = true }
                }
            }
        };

        var detail = MissingTitleDetailMapper.FromMovie(movie, _ => null, _ => null);

        Assert.Equal("official", detail.YoutubeTrailerKey);
    }

    [Fact]
    public void FromMovie_PrefersATrailerTypeOverAnOfficialTeaser()
    {
        var movie = new Movie
        {
            Id = 1,
            Title = "Untitled",
            Videos = new ResultContainer<Video>
            {
                Results = new List<Video>
                {
                    new() { Site = "YouTube", Key = "officialTeaser", Type = "Teaser", Official = true },
                    new() { Site = "YouTube", Key = "unofficialTrailer", Type = "Trailer", Official = false }
                }
            }
        };

        var detail = MissingTitleDetailMapper.FromMovie(movie, _ => null, _ => null);

        Assert.Equal("unofficialTrailer", detail.YoutubeTrailerKey);
    }

    [Fact]
    public void FromSeries_MapsTheFullShape()
    {
        var show = new TvShow
        {
            Id = 1399,
            Name = "Game of Thrones",
            FirstAirDate = new DateTime(2011, 4, 17, 0, 0, 0, DateTimeKind.Utc),
            Tagline = "Winter Is Coming",
            Overview = "Seven noble families...",
            Genres = new List<Genre> { new() { Id = 10765, Name = "Sci-Fi & Fantasy" } },
            EpisodeRunTime = new List<int> { 0, 60 },
            VoteCount = 100,
            VoteAverage = 8.4,
            Status = "Ended",
            NumberOfSeasons = 8,
            Networks = new List<NetworkWithLogo> { new() { Name = "HBO" } },
            PosterPath = "/poster.jpg",
            BackdropPath = "/backdrop.jpg",
            ExternalIds = new ExternalIdsTvShow { ImdbId = "tt0944947" }
        };

        var detail = MissingTitleDetailMapper.FromSeries(show, path => "poster:" + path, path => "backdrop:" + path);

        Assert.Equal("Game of Thrones", detail.Title);
        Assert.Equal("Series", detail.Kind);
        Assert.Equal(1399, detail.TmdbId);
        Assert.Equal(2011, detail.Year);
        Assert.Equal("Winter Is Coming", detail.Tagline);
        Assert.Equal(60, detail.RuntimeMinutes);
        Assert.Equal(8, detail.NumberOfSeasons);
        Assert.Equal(["HBO"], detail.Networks);
        Assert.Equal("https://www.themoviedb.org/tv/1399", detail.TmdbUrl);
        Assert.Equal("https://www.imdb.com/title/tt0944947/", detail.ImdbUrl);
    }

    [Fact]
    public void FromSeries_NumberOfSeasonsIsNullWhenZero()
    {
        var show = new TvShow { Id = 1, Name = "Untitled", NumberOfSeasons = 0 };

        var detail = MissingTitleDetailMapper.FromSeries(show, _ => null, _ => null);

        Assert.Null(detail.NumberOfSeasons);
    }

    [Fact]
    public void FromSeries_RuntimeSkipsLeadingZeroesInTheEpisodeRunTimeList()
    {
        var show = new TvShow { Id = 1, Name = "Untitled", EpisodeRunTime = new List<int> { 0, 0, 45 } };

        var detail = MissingTitleDetailMapper.FromSeries(show, _ => null, _ => null);

        Assert.Equal(45, detail.RuntimeMinutes);
    }

    [Fact]
    public void FromMovie_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => MissingTitleDetailMapper.FromMovie(null!, _ => null, _ => null));
    }

    [Fact]
    public void FromSeries_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => MissingTitleDetailMapper.FromSeries(null!, _ => null, _ => null));
    }
}
