using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Diagnostics;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

// GapDiagnostics explains why a movie, show, album, or book gap is reported missing, and audits the library
// for the same problem in bulk, by diffing a gap against owned items. These drive the internal seams that
// take the owned items directly (no library load). Matching is on normalized title and on a per-kind primary
// id (TheMovieDb for movies/shows, MusicBrainz release-group for albums, OpenLibrary work for books).
public class GapDiagnosticsTests
{
    private static Movie OwnedMovie(string name, int? year, params (string Key, string Value)[] ids)
        => Owned(new Movie(), name, year, ids);

    private static Series OwnedSeries(string name, int? year, params (string Key, string Value)[] ids)
        => Owned(new Series(), name, year, ids);

    private static MusicAlbum OwnedAlbum(string name, int? year, params (string Key, string Value)[] ids)
        => Owned(new MusicAlbum(), name, year, ids);

    private static Book OwnedBook(string name, int? year, params (string Key, string Value)[] ids)
        => Owned(new Book(), name, year, ids);

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
        // A one-year gap between the catalog's year and the library's production year is release-date
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
    public void ApplyCrossProviderDisagreement_DifferentImdb_DowngradesToNotOwned()
    {
        // A same-title owned movie under a different TheMovieDb id, but the resolved IMDb ids differ: a
        // different film sharing the title, so the deeper pass downgrades it to a genuine gap.
        var gap = Gap(BaseItemKind.Movie, "The Game", 1997, ("Tmdb", "1000"), ("Imdb", "tt0119174"));
        var diagnosis = new GapDiagnosis
        {
            Reason = DiagnosisReason.OwnedUnderWrongId,
            TargetKind = BaseItemKind.Movie,
            Candidates = new List<DiagnosisItem>
            {
                new()
                {
                    Relation = "titleMatch",
                    Name = "The Game",
                    ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "55", ["Imdb"] = "tt9999999" }
                }
            }
        };

        GapDiagnostics.ApplyCrossProviderDisagreement(gap, diagnosis);

        Assert.Equal(DiagnosisReason.NotOwned, diagnosis.Reason);
        Assert.Contains("different films", diagnosis.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyCrossProviderDisagreement_MatchingImdb_ConfirmsOwnedUnderWrongId()
    {
        // The same-title owned movie has a different TheMovieDb id but the same IMDb id: confirmed the same
        // film under the wrong id.
        var gap = Gap(BaseItemKind.Movie, "The Game", 1997, ("Tmdb", "1000"), ("Imdb", "tt0119174"));
        var diagnosis = new GapDiagnosis
        {
            Reason = DiagnosisReason.OwnedUnderWrongId,
            TargetKind = BaseItemKind.Movie,
            Candidates = new List<DiagnosisItem>
            {
                new()
                {
                    Relation = "titleMatch",
                    Name = "The Game",
                    ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "55", ["Imdb"] = "tt0119174" }
                }
            }
        };

        GapDiagnostics.ApplyCrossProviderDisagreement(gap, diagnosis);

        Assert.Equal(DiagnosisReason.OwnedUnderWrongId, diagnosis.Reason);
        Assert.Contains("Confirmed", diagnosis.Summary, StringComparison.Ordinal);
        Assert.Contains("IMDb", diagnosis.Candidates[0].Note!, StringComparison.OrdinalIgnoreCase);
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

        var d = GapDiagnostics.DiagnoseAgainst(gap, []);

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

        var d = GapDiagnostics.DiagnoseAgainst(gap, []);

        Assert.NotNull(d.Target);
        Assert.Equal("tt0306414", d.Target!.ProviderIds["Imdb"]);
        // Links come from ProviderLinks (the one canonical source) over the whole id map: a tv TMDB page,
        // a TheTVDB series dereferrer, and an IMDb title.
        Assert.Contains(d.Target.Links, l => l.Url.Contains("themoviedb.org/tv/1438", StringComparison.Ordinal));
        Assert.Contains(d.Target.Links, l => l.Url.Contains("thetvdb.com/dereferrer/series/79126", StringComparison.Ordinal));
        Assert.Contains(d.Target.Links, l => l.Url.Contains("imdb.com/title/tt0306414", StringComparison.Ordinal));
    }

    [Fact]
    public void DiagnoseAgainst_NonDiagnosableKindGap_IsNotDiagnosable()
    {
        // Episodes are not a diagnosable kind (only movies, shows, albums, and books are).
        var gap = Gap(BaseItemKind.Episode, "Pilot", 2010, ("Tmdb", "5"));

        var d = GapDiagnostics.DiagnoseAgainst(gap, []);

        Assert.Null(d.Target);
        Assert.Contains("album, and book", d.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnoseAgainst_AlbumOwnedUnderDifferentReleaseGroup_FlagsMismatch()
    {
        // A music album gap keyed on the MusicBrainz release-group id, owned under a different release-group:
        // diagnosed as owned under the wrong id, with the MusicBrainz label in the message.
        var gap = Gap(BaseItemKind.MusicAlbum, "Kid A", 2000, ("MusicBrainzReleaseGroup", "rg-1"));
        var owned = new BaseItem[] { OwnedAlbum("Kid A", 2000, ("MusicBrainzReleaseGroup", "rg-999")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        Assert.Single(d.Candidates);
        Assert.Equal(DiagnosisReason.OwnedUnderWrongId, d.Reason);
        Assert.Contains("MusicBrainz", d.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnoseAgainst_BookGenuinelyMissing_ReportsGap()
    {
        var gap = Gap(BaseItemKind.Book, "Dune Messiah", 1969, ("OpenLibrary", "OL1W"));
        var owned = new BaseItem[] { OwnedBook("Children of Dune", 1976, ("OpenLibrary", "OL2W")) };

        var d = GapDiagnostics.DiagnoseAgainst(gap, owned);

        Assert.Empty(d.Candidates);
        Assert.Equal(DiagnosisReason.NotOwned, d.Reason);
        Assert.Equal(BaseItemKind.Book, d.TargetKind);
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
        var report = new GapReport { Items = [] };
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
    public void AuditAgainst_SkipsNonDiagnosableGaps()
    {
        var report = new GapReport
        {
            Items = new[]
            {
                Gap(BaseItemKind.Episode, "Pilot", 2010, ("Tmdb", "5")),                          // not diagnosable
                Gap(BaseItemKind.MusicAlbum, "Kid A", 2000, ("MusicBrainzReleaseGroup", "rg-1")), // diagnosable
                Gap(BaseItemKind.Movie, "Heat", 1995, ("Tmdb", "949"))                            // diagnosable
            }
        };

        var audit = GapDiagnostics.AuditAgainst(report, []);

        Assert.Equal(2, audit.GapsChecked); // the album and movie are checked; the episode is skipped
        Assert.Empty(audit.Mismatches);
    }

    [Fact]
    public void AuditAgainst_FindsAlbumMisidentificationAndCountsOwnedAlbums()
    {
        // The gap album is owned under a different MusicBrainz id, so the ownership diff cannot see it.
        var report = new GapReport
        {
            Items = new[] { Gap(BaseItemKind.MusicAlbum, "Kid A", 2000, ("MusicBrainzReleaseGroup", "rg-1")) }
        };
        var owned = new BaseItem[] { OwnedAlbum("Kid A", 2000, ("MusicBrainzReleaseGroup", "rg-999")) };

        var audit = GapDiagnostics.AuditAgainst(report, owned);

        Assert.Equal(1, audit.OwnedAlbums);
        var m = Assert.Single(audit.Mismatches);
        Assert.Equal("Kid A", m.Target!.Name);
    }

    [Fact]
    public void AuditAgainst_DuplicateAlbumIds_UseTheMusicBrainzProvider()
    {
        var report = new GapReport { Items = [] };
        var owned = new BaseItem[]
        {
            OwnedAlbum("Album A", 2001, ("MusicBrainzReleaseGroup", "rg-5")),
            OwnedAlbum("Album B", 2002, ("MusicBrainzReleaseGroup", "rg-5")) // shares the id -> duplicate
        };

        var audit = GapDiagnostics.AuditAgainst(report, owned);

        var dup = Assert.Single(audit.Duplicates);
        Assert.Equal(ProviderIds.MusicBrainzReleaseGroup, dup.Provider);
        Assert.Equal("rg-5", dup.Id);
    }

    [Fact]
    public void AuditAgainst_ScopesToTheRequestedDomain()
    {
        var movie = Gap(BaseItemKind.Movie, "The Thing", 1982, ("Tmdb", "1091"));
        movie.Domain = MediaDomain.Movies;
        var series = Gap(BaseItemKind.Series, "Some Show", 2000, ("Tmdb", "111"));
        series.Domain = MediaDomain.Shows;
        var report = new GapReport { Items = new[] { movie, series } };
        var owned = new BaseItem[] { OwnedSeries("Some Show", 2000, ("Tmdb", "222")) };

        var audit = GapDiagnostics.AuditAgainst(report, owned, MediaDomain.Shows);

        Assert.Equal(1, audit.GapsChecked); // the Movies gap is out of scope
        Assert.Equal("Shows", audit.DomainName);
    }

    [Fact]
    public void DiagnoseSeriesContent_EpisodeWithinOwnedRun_IsGenuineGap()
    {
        var gap = Gap(BaseItemKind.Episode, "Some Show S02E05", 2003);

        var d = GapDiagnostics.DiagnoseSeriesContentAgainst(
            gap, "Some Show", 2000, new Dictionary<string, string> { ["Tmdb"] = "111" }, "series-guid", new[] { 2000, 2001, 2002, 2003 }, [], []);

        Assert.Equal(DiagnosisReason.NotOwned, d.Reason);
        Assert.Contains("genuine", d.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnoseSeriesContent_EpisodeFarOutsideRun_LooksLikeRebootAndSurfacesTheId()
    {
        // Owned "V" is 1984-85; this episode aired 2009 (the reboot) with nothing bridging to it. Flagged, and
        // the series' TheMovieDb id is surfaced as a disambiguation to check, without the verdict depending on it.
        var gap = Gap(BaseItemKind.Episode, "V S02E01", 2009);

        var d = GapDiagnostics.DiagnoseSeriesContentAgainst(
            gap, "V", 1984, new Dictionary<string, string> { ["Tmdb"] = "40063" }, "series-guid", new[] { 1984, 1985 }, [], []);

        Assert.Equal(DiagnosisReason.OwnedUnderWrongId, d.Reason);
        Assert.Contains("reboot", d.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TheMovieDb 40063", d.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnoseSeriesContent_OldEpisodeBridgedByMissingYears_IsGenuineGap()
    {
        // Own 2008-2026 of a long run that began in 1974, with the 1974-2007 seasons surfaced as missing
        // episodes contiguous with the owned run. An aired-1974 episode now bridges into the era, so it reads
        // as a genuine missing episode, not a reboot (the scan's reboot heuristic and the popup agree).
        var gap = Gap(BaseItemKind.Episode, "Long Show S01E01", 1974);
        var missing = Enumerable.Range(1974, 34).ToArray(); // 1974..2007

        var d = GapDiagnostics.DiagnoseSeriesContentAgainst(
            gap, "Long Show", 1974, new Dictionary<string, string> { ["Tmdb"] = "222" }, "series-guid", new[] { 2008, 2026 }, missing, []);

        Assert.Equal(DiagnosisReason.NotOwned, d.Reason);
        Assert.Contains("genuine", d.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1974 to 2026", d.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnoseSeriesContent_RebootWithFarMissingCluster_StaysReboot()
    {
        // Own 2008-2026; the missing years are a far cluster (1974-1980) that does not bridge to the owned run.
        // An aired-1974 episode is outside the era, so it still reads as a same-named reboot.
        var gap = Gap(BaseItemKind.Episode, "Reboot Show S01E01", 1974);

        var d = GapDiagnostics.DiagnoseSeriesContentAgainst(
            gap, "Reboot Show", 1974, new Dictionary<string, string> { ["Tmdb"] = "333" }, "series-guid", new[] { 2008, 2026 }, new[] { 1974, 1975, 1980 }, []);

        Assert.Equal(DiagnosisReason.OwnedUnderWrongId, d.Reason);
        Assert.Contains("reboot", d.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiagnoseSeriesContent_NoOwnedYears_IsGenuineGap()
    {
        var gap = Gap(BaseItemKind.Episode, "New Show S01E01", 2020);

        var d = GapDiagnostics.DiagnoseSeriesContentAgainst(
            gap, "New Show", 2020, new Dictionary<string, string>(), "series-guid", [], [], []);

        Assert.Equal(DiagnosisReason.NotOwned, d.Reason);
    }

    [Fact]
    public void DiagnoseSeriesContent_EpisodeNumberIsOwned_IsNotActuallyMissing()
    {
        // The cross-check reported S01E20 missing, but the library owns that episode number, so it is not a gap
        // at all (stale, or the cross-check numbers it differently). The air-year heuristic alone cannot see this.
        var gap = new GapItem { Id = "seriescontent:show:s01e20", Name = "Some Show S01E20", Year = 1993, TargetKind = BaseItemKind.Episode, ProviderIds = new Dictionary<string, string>() };

        var d = GapDiagnostics.DiagnoseSeriesContentAgainst(
            gap, "Some Show", 1993, new Dictionary<string, string> { ["Tmdb"] = "580" }, "series-guid",
            new[] { 1993, 1999 }, [], new (int, int, string?, int)[] { (1, 19, "A", 1), (1, 20, "In the Hands of the Prophets", 1), (1, 21, "B", 1) });

        Assert.Equal(DiagnosisReason.OwnedUnderWrongId, d.Reason);
        Assert.Contains("not actually missing", d.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("S01E20", d.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnoseSeriesContent_EpisodeNumberNotOwnedWithinRun_IsGenuineAndCitesTheNumber()
    {
        // S01E20 is genuinely absent from the owned numbers and aired within the run, so it is a real gap, and
        // the verdict now names the episode rather than leaning on the air year alone.
        var gap = new GapItem { Id = "seriescontent:show:s01e20", Name = "Some Show S01E20", Year = 1993, TargetKind = BaseItemKind.Episode, ProviderIds = new Dictionary<string, string>() };

        var d = GapDiagnostics.DiagnoseSeriesContentAgainst(
            gap, "Some Show", 1993, new Dictionary<string, string> { ["Tmdb"] = "580" }, "series-guid",
            new[] { 1993, 1999 }, [], new (int, int, string?, int)[] { (1, 1, "X", 1), (1, 2, "Y", 1) });

        Assert.Equal(DiagnosisReason.NotOwned, d.Reason);
        Assert.Contains("genuine", d.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("S01E20", d.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnoseSeriesContent_SameTitleOwnedAtAnotherNumber_IsOwnedUnderWrongNumber()
    {
        // The catalog numbers "In the Hands of the Prophets" S01E20, but the library has it as S01E19, Part 1
        // (two media versions), so the missing number's title matches an owned episode: a false gap, not missing.
        var gap = new GapItem { Id = "seriescontent:show:s01e20", Name = "Star Trek S01E20 - In the Hands of the Prophets", Year = 1993, TargetKind = BaseItemKind.Episode, ProviderIds = new Dictionary<string, string>() };

        var d = GapDiagnostics.DiagnoseSeriesContentAgainst(
            gap, "Star Trek", 1993, new Dictionary<string, string> { ["Tmdb"] = "580" }, "series-guid",
            new[] { 1993, 1999 }, [], new (int, int, string?, int)[] { (1, 18, "Duet", 1), (1, 19, "In the Hands of the Prophets, Part 1", 2) });

        Assert.Equal(DiagnosisReason.OwnedUnderWrongId, d.Reason);
        Assert.Contains("S01E19", d.Summary, StringComparison.Ordinal);
        Assert.Contains("2 versions", d.Summary, StringComparison.Ordinal);
    }
}
