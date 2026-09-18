using System;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using TMDbLib.Objects.Search;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class TmdbSeriesMapperTests
{
    [Fact]
    public void ToCanonical_CopiesSeasonNumberTitleAndAirDate()
    {
        var episodes = new[]
        {
            new TvSeasonEpisode { SeasonNumber = 1, EpisodeNumber = 1, Name = "Pilot", AirDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), Overview = "first", StillPath = "/pilot.jpg" },
            new TvSeasonEpisode { SeasonNumber = 2, EpisodeNumber = 5, Name = "Finale", AirDate = null }
        };

        var canonical = TmdbSeriesMapper.ToCanonical(episodes, path => path is null ? null : "https://image.tmdb.org/t/p/w300" + path);

        Assert.Equal(2, canonical.Count);
        Assert.Equal((1, 1, "Pilot"), (canonical[0].Season, canonical[0].Number, canonical[0].Name));
        Assert.Equal(2020, canonical[0].ReleaseDate?.Year);
        Assert.Equal("https://image.tmdb.org/t/p/w300/pilot.jpg", canonical[0].ImageUrl);
        Assert.Equal((2, 5), (canonical[1].Season, canonical[1].Number));
        Assert.Null(canonical[1].ReleaseDate);
        Assert.Null(canonical[1].ImageUrl);
    }
}
