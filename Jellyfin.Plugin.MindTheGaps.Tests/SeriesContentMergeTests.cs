using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class SeriesContentMergeTests
{
    private static CanonicalEpisode Ep(int season, int number) => new(season, number, $"S{season}E{number}", null, null);

    private static IReadOnlyList<CanonicalEpisode> List(params (int Season, int Number)[] eps)
    {
        var list = new List<CanonicalEpisode>();
        foreach (var (season, number) in eps)
        {
            list.Add(Ep(season, number));
        }

        return list;
    }

    private static int CountSeason(IReadOnlyList<CanonicalEpisode> eps, int season)
    {
        var n = 0;
        foreach (var e in eps)
        {
            if (e.Season == season)
            {
                n++;
            }
        }

        return n;
    }

    [Fact]
    public void Combine_SecondaryFillsSeasonsThePrimaryDoesNotCover_ButNotWithinAClaimedSeason()
    {
        // Priority TMDB, TVDB. TMDB: S1E1-10. TVDB: S1E1-11 and S2E1-5.
        var tmdb = List((1, 1), (1, 2), (1, 3), (1, 4), (1, 5), (1, 6), (1, 7), (1, 8), (1, 9), (1, 10));
        var tvdb = List((1, 1), (1, 2), (1, 3), (1, 4), (1, 5), (1, 6), (1, 7), (1, 8), (1, 9), (1, 10), (1, 11), (2, 1), (2, 2), (2, 3), (2, 4), (2, 5));

        var merged = SeriesContentMerge.Combine(new[] { tmdb, tvdb });

        // S1 is TMDB's (1-10), so TVDB's S1E11 is dropped; S2 is open, so TVDB's S2E1-5 stand.
        Assert.DoesNotContain(merged, e => e.Season == 1 && e.Number == 11);
        Assert.Equal(10, CountSeason(merged, 1));
        Assert.Equal(5, CountSeason(merged, 2));
    }

    [Fact]
    public void Combine_FullySupersededProviderContributesNothing()
    {
        var primary = List((1, 1), (1, 2));
        var secondary = List((1, 1), (1, 2), (1, 3)); // all in S1, which the primary claims

        var merged = SeriesContentMerge.Combine(new[] { primary, secondary });

        Assert.Equal(2, merged.Count); // the secondary's S1E3 is dropped
    }

    [Fact]
    public void Combine_CascadesAcrossThreeProviders()
    {
        var a = List((1, 1));         // claims S1
        var b = List((1, 9), (2, 1)); // S1 taken, claims S2
        var c = List((2, 9), (3, 1)); // S2 taken, claims S3

        var merged = SeriesContentMerge.Combine(new[] { a, b, c });

        Assert.Equal(1, CountSeason(merged, 1));
        Assert.Equal(1, CountSeason(merged, 2));
        Assert.Equal(1, CountSeason(merged, 3));
        Assert.Equal(3, merged.Count);
    }

    [Fact]
    public void Combine_SkipsSpecialsAndEmptyLists()
    {
        var merged = SeriesContentMerge.Combine(new[] { List(), List((0, 1), (1, 1)) });

        Assert.DoesNotContain(merged, e => e.Season == 0);
        Assert.Single(merged);
    }
}
