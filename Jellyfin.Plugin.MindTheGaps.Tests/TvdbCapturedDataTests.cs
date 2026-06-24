using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Jellyfin.Plugin.MindTheGaps.Services.Tvdb;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// Real responses captured from api4.thetvdb.com for Blake's 7 (series 75565).
public class TvdbCapturedDataTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void RemoteIdSearch_ResolvesSeriesId()
    {
        var response = JsonSerializer.Deserialize<TvdbRemoteIdResponse>(TestData.Read("tvdb_remoteid.json"), Options);

        var seriesId = response?.Data?.FirstOrDefault(r => r.Series is not null)?.Series?.Id;
        Assert.Equal(75565, seriesId);
    }

    [Fact]
    public void Episodes_ParseSinglePage()
    {
        var response = JsonSerializer.Deserialize<TvdbEpisodesResponse>(TestData.Read("tvdb_episodes.json"), Options);

        Assert.NotNull(response?.Data?.Episodes);
        Assert.Equal(85, response!.Data!.Episodes!.Count);
        Assert.Null(response.Links?.Next);
    }

    [Fact]
    public void Diff_SkipsSpecials_KeepsNumberedSeasons()
    {
        var response = JsonSerializer.Deserialize<TvdbEpisodesResponse>(TestData.Read("tvdb_episodes.json"), Options)!;
        var canonical = TvdbMapper.ToCanonical(response.Data!.Episodes!);

        // The mapper keeps season-0 specials; the diff is what drops them.
        Assert.Contains(canonical, e => e.Season == 0);

        var missing = SeriesContentDiff.Missing(canonical, new OwnedEpisodes(), 1000);
        Assert.Equal(52, missing.Count);
        Assert.All(missing, e => Assert.True(e.Season >= 1));

        var first = missing[0];
        Assert.Equal(1, first.Season);
        Assert.Equal(1, first.Number);
        Assert.Equal("The Way Back", first.Name);
    }
}
