using System;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

public class SeriesContentDiffTests
{
    private static CanonicalEpisode Ep(int season, int number)
        => new(season, number, $"E{number}", null, null);

    private static OwnedEpisodes Owned(params (int Season, int Number)[] numbers)
    {
        var owned = new OwnedEpisodes();
        foreach (var (season, number) in numbers)
        {
            owned.AddNumber(season, number);
        }

        return owned;
    }

    [Fact]
    public void Missing_ReturnsOnlyUnownedEpisodes()
    {
        var canonical = new[] { Ep(1, 1), Ep(1, 2), Ep(1, 3) };

        var missing = SeriesContentDiff.Missing(canonical, Owned((1, 1), (1, 3)), 100);

        var episode = Assert.Single(missing);
        Assert.Equal((1, 2), (episode.Season, episode.Number));
    }

    [Fact]
    public void Missing_SkipsSpecialsAndUnnumbered()
    {
        var canonical = new[] { Ep(0, 1), Ep(1, 0), Ep(1, 1) };

        var missing = SeriesContentDiff.Missing(canonical, Owned(), 100);

        var episode = Assert.Single(missing);
        Assert.Equal((1, 1), (episode.Season, episode.Number));
    }

    [Fact]
    public void Missing_DeDupesRepeatedNumbers()
    {
        var canonical = new[] { Ep(1, 1), Ep(1, 1) };

        Assert.Single(SeriesContentDiff.Missing(canonical, Owned(), 100));
    }

    [Fact]
    public void Missing_RespectsCap()
    {
        var canonical = new[] { Ep(1, 1), Ep(1, 2), Ep(1, 3), Ep(1, 4) };

        Assert.Equal(2, SeriesContentDiff.Missing(canonical, Owned(), 2).Count);
    }

    [Fact]
    public void Missing_AllOwned_ReturnsEmpty()
    {
        var canonical = new[] { Ep(1, 1), Ep(1, 2) };

        Assert.Empty(SeriesContentDiff.Missing(canonical, Owned((1, 1), (1, 2)), 100));
    }

    // The source renumbers a season the library follows from another provider: every episode lands at a
    // different number, but the air dates line up, so none should read as missing (the Aeon Flux case).
    [Fact]
    public void Missing_RenumberedSeasonMatchesByAirDate()
    {
        var d1 = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var d2 = new DateTime(2020, 1, 8, 0, 0, 0, DateTimeKind.Utc);
        var canonical = new[]
        {
            new CanonicalEpisode(3, 1, "A", d1, null),
            new CanonicalEpisode(3, 2, "B", d2, null)
        };
        var owned = new OwnedEpisodes();
        owned.AddNumber(3, 5);
        owned.AddNumber(3, 6);
        owned.AddAirDate(d1);
        owned.AddAirDate(d2);

        Assert.Empty(SeriesContentDiff.Missing(canonical, owned, 100));
    }

    // The source splits a two-part finale (E23 + E24) the library merged into one file numbered E23 and
    // titled as the single catalogue episode. The split tail folds to the owned title, so it is not missing.
    [Fact]
    public void Missing_TwoPartTailMatchesOwnedMergedTitle()
    {
        var canonical = new[]
        {
            new CanonicalEpisode(1, 23, "The Finale (1)", null, null),
            new CanonicalEpisode(1, 24, "The Finale (2)", null, null)
        };
        var owned = new OwnedEpisodes();
        owned.AddNumber(1, 23);
        owned.AddTitle(1, "The Finale");

        Assert.Empty(SeriesContentDiff.Missing(canonical, owned, 100));
    }

    // Reconciliation must not swallow a genuinely missing episode: a different title on a different date at
    // an unowned number is still reported.
    [Fact]
    public void Missing_UnrelatedEpisodeStillReported()
    {
        var owned = new OwnedEpisodes();
        owned.AddNumber(1, 1);
        owned.AddAirDate(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        owned.AddTitle(1, "Pilot");
        var canonical = new[]
        {
            new CanonicalEpisode(1, 1, "Pilot", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), null),
            new CanonicalEpisode(1, 2, "Brand New", new DateTime(2020, 1, 8, 0, 0, 0, DateTimeKind.Utc), null)
        };

        var episode = Assert.Single(SeriesContentDiff.Missing(canonical, owned, 100));
        Assert.Equal((1, 2), (episode.Season, episode.Number));
    }

    private static CanonicalEpisode EpAired(int season, int number, int year)
        => new(season, number, $"E{number}", new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc), null);

    [Fact]
    public void LooksLikeDifferentSeries_RebootYearFarFromOwned_IsFlagged()
    {
        // Owned "V" is the 1984 series; the resolved show's first season aired 2009 (the reboot).
        var canonical = new[] { EpAired(1, 1, 2009), EpAired(2, 1, 2010), EpAired(3, 1, 2011) };

        Assert.True(SeriesContentGapSourceBase.LooksLikeDifferentSeries(1984, canonical));
    }

    [Fact]
    public void LooksLikeDifferentSeries_SameStartYear_NotFlagged_EvenWithLaterSeasons()
    {
        // A legitimate long run: season one matches the owned start year; later seasons years later do not
        // flag it (the guard compares the lowest season's year, not every episode's).
        var canonical = new[] { EpAired(1, 1, 2000), EpAired(2, 1, 2001), EpAired(10, 1, 2010) };

        Assert.False(SeriesContentGapSourceBase.LooksLikeDifferentSeries(2000, canonical));
    }

    [Fact]
    public void LooksLikeDifferentSeries_NoOwnedYear_NotFlagged()
    {
        Assert.False(SeriesContentGapSourceBase.LooksLikeDifferentSeries(null, new[] { EpAired(1, 1, 2009) }));
    }
}
