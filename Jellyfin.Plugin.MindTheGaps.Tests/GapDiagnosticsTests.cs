using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Diagnostics;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// GapDiagnostics explains why a movie/show gap is reported missing, and audits the library for the same
// problem in bulk, by diffing a gap against owned items. These drive the internal seams that take the owned
// items directly (no library load). Matching is TheMovieDb-keyed, on normalized title and on id.
public class GapDiagnosticsTests
{
    private static Movie OwnedMovie(string name, int? year, params (string Key, string Value)[] ids)
        => Owned(new Movie(), name, year, ids);

    private static Series OwnedSeries(string name, int? year, params (string Key, string Value)[] ids)
        => Owned(new Series(), name, year, ids);

    private static T Owned<T>(T item, string name, int? year, (string Key, string Value)[] ids)
        where T : BaseItem
    {
        item.Name = name;
        item.ProductionYear = year;
        item.Id = Guid.NewGuid();
        item.ProviderIds = ids.ToDictionary(p => p.Key, p => p.Value);
        return item;
    }

    private static GapItem Gap(BaseItemKind kind, string name, int? year, params (string Key, string Value)[] ids) => new()
    {
        Id = "gap-" + name,
        Name = name,
        Year = year,
        TargetKind = kind,
        ProviderIds = ids.ToDictionary(p => p.Key, p => p.Value)
    };

    [Fact]
    public void DiagnoseAgainst_NotOwned_ReportsGenuineGap()
    {
        var gap = Gap(BaseItemKind.Movie, "Heat", 1995, ("Tmdb", "949"));
        var owned = new BaseItem[] { OwnedMovie("Casino", 1995, ("Tmdb", "524")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        Assert.Empty(d.Candidates);
        Assert.Equal(DiagnosisReason.NotOwned, d.Reason);
        Assert.Contains("genuine gap", d.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BaseItemKind.Movie, d.TargetKind);
    }

    [Fact]
    public void DiagnoseAgainst_OwnedUnderDifferentId_FlagsTitleMismatch()
    {
        var gap = Gap(BaseItemKind.Movie, "The Thing", 1982, ("Tmdb", "1091"));
        var owned = new BaseItem[] { OwnedMovie("The Thing", 1982, ("Tmdb", "999999")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        var c = Assert.Single(d.Candidates);
        Assert.Equal("titleMatch", c.Relation);
        Assert.Equal("999999", c.ProviderIds["Tmdb"]);
        Assert.Contains("different id", c.Note!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DiagnosisReason.OwnedUnderWrongId, d.Reason);
        Assert.Contains("mismatch", d.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnoseAgainst_RemakeSharesTitleButYearDiffers_IsGenuineGap()
    {
        // The missing 1960 original shares its title with the owned 2001 remake. Years far apart mean a
        // different release, so this is a genuine gap, not the remake owned under the wrong id.
        var gap = Gap(BaseItemKind.Movie, "Ocean's Eleven", 1960, ("Tmdb", "21786"));
        var owned = new BaseItem[] { OwnedMovie("Ocean's Eleven", 2001, ("Tmdb", "161")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        Assert.Empty(d.Candidates);
        Assert.Equal(DiagnosisReason.NotOwned, d.Reason);
    }

    [Fact]
    public void DiagnoseAgainst_RebootSharesTitleWithOwnedOriginal_IsGenuineGap()
    {
        // The missing 2023 reboot shares its title with the owned 1990 original; the year tells them apart.
        var gap = Gap(BaseItemKind.Movie, "Home Alone", 2023, ("Tmdb", "1009408"));
        var owned = new BaseItem[] { OwnedMovie("Home Alone", 1990, ("Tmdb", "771")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        Assert.Empty(d.Candidates);
        Assert.Equal(DiagnosisReason.NotOwned, d.Reason);
    }

    [Fact]
    public void DiagnoseAgainst_SameTitleYearJitter_StillFlagsMismatch()
    {
        // A one-year gap between the catalogue's year and the library's production year is release-date
        // jitter, not a remake, so the mismatch is still caught.
        var gap = Gap(BaseItemKind.Movie, "Coco", 2017, ("Tmdb", "354912"));
        var owned = new BaseItem[] { OwnedMovie("Coco", 2018, ("Tmdb", "999999")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        Assert.Single(d.Candidates);
        Assert.Equal(DiagnosisReason.OwnedUnderWrongId, d.Reason);
    }

    [Fact]
    public void DiagnoseAgainst_PrefersExactYearOverOneYearOff()
    {
        // The Game (1997) thriller versus The Game (1998) comedy: when the exact-year copy is owned (under a
        // wrong id), only it is a candidate. The one-year-off owned item is a different film, not a near match.
        var gap = Gap(BaseItemKind.Movie, "The Game", 1997, ("Tmdb", "1000"));
        var owned = new BaseItem[]
        {
            OwnedMovie("The Game", 1997, ("Tmdb", "999999")),
            OwnedMovie("The Game", 1998, ("Tmdb", "55"))
        };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        var c = Assert.Single(d.Candidates);
        Assert.Equal(1997, c.Year);
        Assert.Equal(DiagnosisReason.OwnedUnderWrongId, d.Reason);
    }

    [Fact]
    public void DiagnoseAgainst_SameTitleYearMissing_FallsBackToNameMatch()
    {
        // The owned item has no year, so year cannot tell them apart: fall back to the name match.
        var gap = Gap(BaseItemKind.Movie, "Dune", 2021, ("Tmdb", "438631"));
        var owned = new BaseItem[] { OwnedMovie("Dune", null, ("Tmdb", "999999")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        Assert.Single(d.Candidates);
        Assert.Equal(DiagnosisReason.OwnedUnderWrongId, d.Reason);
    }

    [Fact]
    public void DiagnoseAgainst_OwnedSameTitleNoId_NotesMissingId()
    {
        var gap = Gap(BaseItemKind.Movie, "Solaris", 1972, ("Tmdb", "27328"));
        var owned = new BaseItem[] { OwnedMovie("Solaris", 1972) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        var c = Assert.Single(d.Candidates);
        Assert.Equal("titleMatch", c.Relation);
        Assert.Contains("no TheMovieDb id", c.Note!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnoseAgainst_OwnedSameTitleAndId_LooksStale()
    {
        var gap = Gap(BaseItemKind.Movie, "Alien", 1979, ("Tmdb", "348"));
        var owned = new BaseItem[] { OwnedMovie("Alien", 1979, ("Tmdb", "348")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        var c = Assert.Single(d.Candidates);
        Assert.Equal(DiagnosisReason.Stale, d.Reason);
        Assert.Contains("stale", d.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stale", c.Note!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnoseAgainst_OwnedCarriesIdUnderDifferentTitle_FlagsIdHolder()
    {
        // The owned movie carries the gap's id but a different title: a misidentification.
        var gap = Gap(BaseItemKind.Movie, "Predator", 1987, ("Tmdb", "106"));
        var owned = new BaseItem[] { OwnedMovie("Not Predator", 1987, ("Tmdb", "106")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        var c = Assert.Single(d.Candidates);
        Assert.Equal("idHolder", c.Relation);
        Assert.Equal(DiagnosisReason.CarriesAnothersId, d.Reason);
    }

    [Fact]
    public void DiagnoseAgainst_CorroboratesBySecondaryIdWhenTitleDiffers()
    {
        // A localized title would not match by title, but the shared IMDb id still catches the owned item,
        // which sits under a different TheMovieDb id.
        var gap = Gap(BaseItemKind.Movie, "The Wages of Fear", 1953, ("Tmdb", "1000"), ("Imdb", "tt0046268"));
        var owned = new BaseItem[] { OwnedMovie("Le salaire de la peur", 1953, ("Tmdb", "999"), ("Imdb", "tt0046268")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        var c = Assert.Single(d.Candidates);
        Assert.Equal("idMatch", c.Relation);
        Assert.Equal(DiagnosisReason.OwnedUnderWrongId, d.Reason);
    }

    [Fact]
    public void DiagnoseAgainst_WrongClassImdbId_IsFlagged()
    {
        // An IMDb person id (nm...) where a title id (tt...) belongs: the match never had a chance.
        var gap = Gap(BaseItemKind.Movie, "Whatever", 2000, ("Imdb", "nm0000123"));

        var d = GapDiagnostics.DiagnoseAgainst(gap, Array.Empty<BaseItem>());

        Assert.Empty(d.Candidates);
        Assert.Equal(DiagnosisReason.WrongIdClass, d.Reason);
        Assert.Contains("person id", d.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnoseAgainst_NormalizesTitleForMatching()
    {
        // Punctuation, case, and spacing differences must not hide an owned item.
        var gap = Gap(BaseItemKind.Movie, "WALL-E", 2008, ("Tmdb", "10681"));
        var owned = new BaseItem[] { OwnedMovie("wall e", 2008, ("Tmdb", "55")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        Assert.Single(d.Candidates); // matched despite "WALL-E" vs "wall e"
    }

    [Fact]
    public void DiagnoseAgainst_MatchesWithinKind_NotAcrossKinds()
    {
        // An owned series sharing a movie gap's title (the Fargo film vs the Fargo series) is not a candidate.
        var gap = Gap(BaseItemKind.Movie, "Fargo", 1996, ("Tmdb", "275"));
        var owned = new BaseItem[] { OwnedSeries("Fargo", 2014, ("Tmdb", "60622")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        Assert.Empty(d.Candidates);
    }

    [Fact]
    public void DiagnoseAgainst_TargetCarriesProviderMapAndCanonicalLinks()
    {
        var gap = Gap(BaseItemKind.Series, "The Wire", 2002, ("Tmdb", "1438"), ("Imdb", "tt0306414"), ("Tvdb", "79126"));

        var d = GapDiagnostics.DiagnoseAgainst(gap, Array.Empty<BaseItem>());

        Assert.NotNull(d.Target);
        Assert.Equal("tt0306414", d.Target!.ProviderIds["Imdb"]);
        // Links come from ProviderLinks (the one canonical source) over the whole id map: a tv TMDB page,
        // a TheTVDB series dereferrer, and an IMDb title.
        Assert.Contains(d.Target.Links, l => l.Url.Contains("themoviedb.org/tv/1438", StringComparison.Ordinal));
        Assert.Contains(d.Target.Links, l => l.Url.Contains("thetvdb.com/dereferrer/series/79126", StringComparison.Ordinal));
        Assert.Contains(d.Target.Links, l => l.Url.Contains("imdb.com/title/tt0306414", StringComparison.Ordinal));
    }

    [Fact]
    public void DiagnoseAgainst_NonMovieOrSeriesGap_IsNotDiagnosable()
    {
        var gap = Gap(BaseItemKind.Episode, "Pilot", 2010, ("Tmdb", "5"));

        var d = GapDiagnostics.DiagnoseAgainst(gap, Array.Empty<BaseItem>());

        Assert.Null(d.Target);
        Assert.Contains("movie and show", d.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuditAgainst_CollectsMismatchesAndCountsOwnedAndKeepsScanTime()
    {
        var scan = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var report = new GapReport
        {
            GeneratedUtc = scan,
            Items = new[]
            {
                Gap(BaseItemKind.Movie, "The Thing", 1982, ("Tmdb", "1091")), // owned under a wrong id -> mismatch
                Gap(BaseItemKind.Movie, "Heat", 1995, ("Tmdb", "949"))        // genuinely missing
            }
        };
        var owned = new BaseItem[]
        {
            OwnedMovie("The Thing", 1982, ("Tmdb", "999999")),
            OwnedSeries("Some Show", 2000, ("Tmdb", "111"))
        };

        var audit = GapDiagnostics.AuditAgainst(report, owned);

        Assert.Equal(scan, audit.GeneratedUtc);
        Assert.Equal(2, audit.GapsChecked);
        Assert.Equal(1, audit.OwnedMovies);
        Assert.Equal(1, audit.OwnedShows);
        var m = Assert.Single(audit.Mismatches);
        Assert.Equal("The Thing", m.Target!.Name);
    }

    [Fact]
    public void AuditAgainst_FindsDuplicateProviderIds()
    {
        var report = new GapReport { Items = Array.Empty<GapItem>() };
        var owned = new BaseItem[]
        {
            OwnedMovie("Movie A", 2001, ("Tmdb", "500")),
            OwnedMovie("Movie B", 2002, ("Tmdb", "500")), // shares the id -> duplicate
            OwnedMovie("Movie C", 2003, ("Tmdb", "501"))
        };

        var audit = GapDiagnostics.AuditAgainst(report, owned);

        var dup = Assert.Single(audit.Duplicates);
        Assert.Equal("500", dup.Id);
        Assert.Equal(2, dup.Items.Count);
    }

    [Fact]
    public void AuditAgainst_SkipsNonMovieOrSeriesGaps()
    {
        var report = new GapReport
        {
            Items = new[]
            {
                Gap(BaseItemKind.Episode, "Pilot", 2010, ("Tmdb", "5")),
                Gap(BaseItemKind.Movie, "Heat", 1995, ("Tmdb", "949"))
            }
        };

        var audit = GapDiagnostics.AuditAgainst(report, Array.Empty<BaseItem>());

        Assert.Equal(1, audit.GapsChecked); // the episode gap is skipped
        Assert.Empty(audit.Mismatches);
    }
}
