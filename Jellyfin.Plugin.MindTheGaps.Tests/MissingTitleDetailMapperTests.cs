using System;
using System.Collections.Generic;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.WebUi;
using Jellyfin.Plugin.MindTheGaps.Services.Acquisition;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.TvShows;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class MissingTitleDetailMapperTests
{
    private static string? Poster(string? p) => p is null ? null : "https://image.tmdb.org/t/p/w500" + p;

    private static string? Backdrop(string? p) => p is null ? null : "https://image.tmdb.org/t/p/w1280" + p;

    private static GapItem Gap(BaseItemKind kind) => new()
    {
        Id = kind == BaseItemKind.Movie ? "filmography:movie:13" : "filmography:series:1408",
        TargetKind = kind,
        Name = "Fallback name",
        Year = 1990,
        Overview = "as Someone",
        ImageUrl = "https://image.tmdb.org/t/p/w500/gap.jpg",
        ProviderIds = new Dictionary<string, string> { ["Tmdb"] = kind == BaseItemKind.Movie ? "13" : "1408" }
    };

    [Fact]
    public void FromMovie_ShapesWhatTheDialogShows()
    {
        var movie = new Movie
        {
            Id = 13,
            Title = "Forrest Gump",
            ReleaseDate = new DateTime(1994, 7, 6),
            Tagline = "  The world will never be the same once you've seen it through the eyes of Forrest Gump. ",
            Overview = "A man with a low IQ...",
            Runtime = 142,
            Genres = [new Genre { Id = 35, Name = "Comedy" }, new Genre { Id = 18, Name = "Drama" }],
            VoteAverage = 8.4712,
            VoteCount = 25000,
            Status = "Released",
            PosterPath = "/p.jpg",
            BackdropPath = "/b.jpg",
            ExternalIds = new ExternalIdsMovie { ImdbId = "tt0109830" },
            Videos = new ResultContainer<Video> { Results = [new Video { Site = "YouTube", Type = "Trailer", Key = "bLvqoHBptjg", Official = true }] }
        };

        var d = MissingTitleDetailMapper.FromMovie(Gap(BaseItemKind.Movie), movie, "as Someone", null, Poster, Backdrop);

        Assert.Equal("filmography:movie:13", d.GapId);
        Assert.Equal("Movie", d.Kind);
        Assert.Equal("Forrest Gump", d.Title);
        Assert.Equal(1994, d.Year);
        Assert.StartsWith("The world", d.Tagline, StringComparison.Ordinal);
        Assert.Equal(142, d.RuntimeMinutes);
        Assert.Equal(["Comedy", "Drama"], d.Genres);
        Assert.Equal(8.5, d.Rating);
        Assert.Equal(25000, d.VoteCount);
        Assert.Equal("as Someone", d.Role);
        Assert.Equal("https://image.tmdb.org/t/p/w500/p.jpg", d.PosterUrl);
        Assert.Equal("https://image.tmdb.org/t/p/w1280/b.jpg", d.BackdropUrl);
        Assert.Equal("https://www.themoviedb.org/movie/13", d.TmdbUrl);
        Assert.Equal("https://www.imdb.com/title/tt0109830/", d.ImdbUrl);
        Assert.Equal("https://www.youtube.com/watch?v=bLvqoHBptjg", d.TrailerUrl);
        Assert.Null(d.Seasons);
    }

    [Fact]
    public void FromMovie_FallsBackToTheGap_WhenTmdbIsSparse()
    {
        var movie = new Movie { Id = 13, Title = string.Empty, VoteCount = 0, VoteAverage = 0, Runtime = 0, Tagline = "  " };
        var d = MissingTitleDetailMapper.FromMovie(Gap(BaseItemKind.Movie), movie, "as Someone", null, Poster, Backdrop);

        Assert.Equal("Fallback name", d.Title);
        Assert.Equal(1990, d.Year);
        Assert.Null(d.Rating);
        Assert.Null(d.RuntimeMinutes);
        Assert.Null(d.Tagline);
        Assert.Null(d.Overview);
        Assert.Empty(d.Genres);
        Assert.Equal("https://image.tmdb.org/t/p/w500/gap.jpg", d.PosterUrl);
        Assert.Null(d.ImdbUrl);
        Assert.Null(d.TrailerUrl);
    }

    [Fact]
    public void FromSeries_CarriesSeasonsEpisodesNetworkAndEpisodeRuntime()
    {
        var show = new TvShow
        {
            Id = 1408,
            Name = "House",
            FirstAirDate = new DateTime(2004, 11, 16),
            NumberOfSeasons = 8,
            NumberOfEpisodes = 176,
            EpisodeRunTime = [0, 44],
            Networks = [new NetworkWithLogo { Name = "FOX" }],
            Status = "Ended",
            VoteAverage = 8.6,
            VoteCount = 5000,
            ExternalIds = new ExternalIdsTvShow { ImdbId = "tt0412142" }
        };

        var d = MissingTitleDetailMapper.FromSeries(Gap(BaseItemKind.Series), show, null, "Because you have Fargo", Poster, Backdrop);

        Assert.Equal("Series", d.Kind);
        Assert.Equal("House", d.Title);
        Assert.Equal(2004, d.Year);
        Assert.Equal(8, d.Seasons);
        Assert.Equal(176, d.Episodes);
        Assert.Equal("FOX", d.Network);
        Assert.Equal(44, d.RuntimeMinutes);
        Assert.Equal("Ended", d.Status);
        Assert.Equal("https://www.themoviedb.org/tv/1408", d.TmdbUrl);
        Assert.Equal("https://www.imdb.com/title/tt0412142/", d.ImdbUrl);
        Assert.Null(d.Role);
        Assert.Equal("Because you have Fargo", d.Because);
    }

    [Fact]
    public void TrailerUrl_PrefersOfficialTrailer_ThenTrailer_ThenTeaser_OnlyYouTube()
    {
        Assert.Null(MissingTitleDetailMapper.TrailerUrl(null));
        Assert.Null(MissingTitleDetailMapper.TrailerUrl([new Video { Site = "Vimeo", Type = "Trailer", Key = "v" }]));
        Assert.Null(MissingTitleDetailMapper.TrailerUrl([new Video { Site = "YouTube", Type = "Featurette", Key = "f" }]));

        var videos = new[]
        {
            new Video { Site = "YouTube", Type = "Teaser", Key = "teaser", Official = true },
            new Video { Site = "YouTube", Type = "Trailer", Key = "fan", Official = false },
            new Video { Site = "YouTube", Type = "Trailer", Key = "official", Official = true }
        };
        Assert.Equal("https://www.youtube.com/watch?v=official", MissingTitleDetailMapper.TrailerUrl(videos));
        Assert.Equal("https://www.youtube.com/watch?v=fan", MissingTitleDetailMapper.TrailerUrl(videos[..2]));
        Assert.Equal("https://www.youtube.com/watch?v=teaser", MissingTitleDetailMapper.TrailerUrl(videos[..1]));
    }

    [Fact]
    public void ParseProfiles_ReadsIdAndName_SkipsMalformedEntries()
    {
        using var doc = JsonDocument.Parse("""
            [
              { "id": 1, "name": "Any", "upgradeAllowed": true },
              { "id": 5, "name": "Ultra-HD" },
              { "id": 0, "name": "zero id" },
              { "id": 7 },
              { "id": "8", "name": "string id" },
              { "id": 9, "name": "   " },
              "not an object"
            ]
            """);

        var profiles = AcquisitionService.ParseProfiles(doc.RootElement);

        Assert.Equal(2, profiles.Count);
        Assert.Equal((1, "Any"), (profiles[0].Id, profiles[0].Name));
        Assert.Equal((5, "Ultra-HD"), (profiles[1].Id, profiles[1].Name));

        using var notArray = JsonDocument.Parse("""{ "id": 1, "name": "Any" }""");
        Assert.Empty(AcquisitionService.ParseProfiles(notArray.RootElement));
    }
}
