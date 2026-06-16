using System.Collections.Generic;
using System.Text.Json;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Jellyfin.Plugin.MindTheGaps.Services.TvMaze;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Real responses captured from api.tvmaze.com for Blake's 7 (show 6152).
public class TvMazeCapturedDataTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Lookup_ParsesShowId()
    {
        var show = JsonSerializer.Deserialize<TvMazeShow>(TestData.Read("tvmaze_lookup.json"), Options);

        Assert.NotNull(show);
        Assert.Equal(6152, show!.Id);
    }

    [Fact]
    public void Episodes_ParseAndMapToCanonical()
    {
        var episodes = JsonSerializer.Deserialize<List<TvMazeEpisode>>(TestData.Read("tvmaze_episodes.json"), Options);
        Assert.NotNull(episodes);
        Assert.Equal(52, episodes!.Count);

        var canonical = TvMazeMapper.ToCanonical(episodes);
        Assert.Equal(52, canonical.Count);

        var first = canonical[0];
        Assert.Equal(1, first.Season);
        Assert.Equal(1, first.Number);
        Assert.Equal("The Way Back", first.Name);
        Assert.Equal(1978, first.ReleaseDate?.Year);
    }

    [Fact]
    public void Diff_AgainstPartialLibrary_ReportsOnlyMissing()
    {
        var episodes = JsonSerializer.Deserialize<List<TvMazeEpisode>>(TestData.Read("tvmaze_episodes.json"), Options)!;
        var canonical = TvMazeMapper.ToCanonical(episodes);

        var owned = new HashSet<(int Season, int Number)> { (1, 1), (1, 2), (1, 3) };
        var missing = SeriesContentDiff.Missing(canonical, owned, 1000);

        Assert.Equal(canonical.Count - 3, missing.Count);
        Assert.DoesNotContain(missing, e => e.Season == 1 && e.Number <= 3);
    }
}
