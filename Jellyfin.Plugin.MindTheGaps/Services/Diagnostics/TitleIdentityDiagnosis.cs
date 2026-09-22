using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Services.Diagnostics;

/// <summary>
/// The movie/show/album/book half of <see cref="GapDiagnostics"/>: given a gap (or a whole report) and the
/// owned items of its kind, decides whether it looks like a real gap or a metadata mismatch (already owned
/// under a different or missing id). Pure and standalone, like <see cref="Gaps.StaleOwnerPruner"/>: every
/// decision here depends only on the gap(s) and the owned items handed in, never on the library or a
/// network call, which is what makes it unit-testable without either. <see cref="GapDiagnostics"/> is the
/// only caller, and owns everything that does need the library or TheMovieDb (loading the owned set,
/// resolving external ids for the deeper pass, the episode/season path in <see cref="SeriesContentDiagnosis"/>).
/// </summary>
internal static class TitleIdentityDiagnosis
{
    // The secondary ids the diagnosis corroborates a gap against (the primary key stays TheMovieDb).
    private static readonly string[] SecondaryIdProviders = { ProviderIds.Imdb, ProviderIds.Tvdb };

    // Diagnose a gap against an explicit set of owned items: the testable seam, no library load. The public
    // entry supplies the owned movies/shows; tests supply their own.
    public static GapDiagnosis DiagnoseAgainst(GapItem gap, IReadOnlyList<BaseItem> owned)
    {
        if (!IsDiagnosable(gap.TargetKind))
        {
            return new GapDiagnosis { Summary = "Identification diagnosis is available for movie, show, album, and book gaps only." };
        }

        return Evaluate(gap, BuildIndex(owned));
    }

    // Audit a report against an explicit set of owned items: the testable seam, no library load.
    public static IdentificationAudit AuditAgainst(GapReport report, IReadOnlyList<BaseItem> owned, MediaDomain? domain = null, GapPattern? pattern = null)
    {
        var index = BuildIndex(owned);

        var mismatches = new List<GapDiagnosis>();
        var checkedCount = 0;
        foreach (var gap in report.Items)
        {
            if (!IsDiagnosable(gap.TargetKind))
            {
                continue;
            }

            // Scope to the dashboard's current view, so an export from Shows is not full of movie findings.
            if (domain is { } scopeDomain && gap.Domain != scopeDomain)
            {
                continue;
            }

            if (pattern is { } scopePattern && gap.Pattern != scopePattern)
            {
                continue;
            }

            checkedCount++;
            var diagnosis = Evaluate(gap, index);

            // The verdict already says whether this is a real misidentification (owned under the wrong id, or
            // an owned item carrying this id under another title), so the audit just keys off it.
            if (diagnosis.Reason is DiagnosisReason.OwnedUnderWrongId or DiagnosisReason.CarriesAnothersId)
            {
                mismatches.Add(diagnosis);
            }
        }

        // Each owned item is indexed by its kind's primary id (TheMovieDb for movies/shows, MusicBrainz for
        // albums, OpenLibrary for books), so a duplicate group's provider is the one for its kind.
        var duplicates = new List<DuplicateIdGroup>();
        foreach (var pair in index.ByPrimaryId)
        {
            if (pair.Value.Count < 2)
            {
                continue;
            }

            duplicates.Add(new DuplicateIdGroup
            {
                Provider = PrimaryProvider(pair.Key.Kind),
                Id = pair.Key.Id,
                TargetKind = pair.Key.Kind,
                Items = pair.Value.Select(o => ToItem(o, "owned", null)).ToList()
            });
        }

        return new IdentificationAudit
        {
            // The audit does no fresh discovery; it analyzes the report, so it carries the report's scan time.
            GeneratedUtc = report.GeneratedUtc,
            DomainName = domain?.ToString(),
            PatternName = pattern?.ToString(),
            OwnedMovies = index.All.Count(o => o.Kind == BaseItemKind.Movie),
            OwnedShows = index.All.Count(o => o.Kind == BaseItemKind.Series),
            OwnedAlbums = index.All.Count(o => o.Kind == BaseItemKind.MusicAlbum),
            OwnedBooks = index.All.Count(o => o.Kind == BaseItemKind.Book),
            GapsChecked = checkedCount,
            Mismatches = mismatches,
            Duplicates = duplicates
        };
    }

    // In the deeper pass, compare the gap's resolved IMDb id with each same-title owned candidate's resolved
    // IMDb id. A match confirms the candidate is the same film under the wrong TheMovieDb id; a mismatch means
    // a different film that merely shares the title. When every same-title candidate is a different film (and
    // nothing matched by a shared id), the gap is genuinely missing after all. Does nothing without a gap IMDb
    // id to compare, so it is safe to call whenever the deeper pass ran.
    public static void ApplyCrossProviderDisagreement(GapItem gap, GapDiagnosis diagnosis)
    {
        gap.ProviderIds.TryGetValue(ProviderIds.Imdb, out var gapImdb);
        gap.ProviderIds.TryGetValue(ProviderIds.Tmdb, out var gapTmdb);
        if (string.IsNullOrEmpty(gapImdb))
        {
            return;
        }

        var titleMatches = diagnosis.Candidates
            .Where(c => string.Equals(c.Relation, "titleMatch", StringComparison.Ordinal))
            .ToList();
        if (titleMatches.Count == 0)
        {
            return;
        }

        var hasSharedIdMatch = diagnosis.Candidates.Any(c => c.Relation is "idMatch" or "idHolder");
        var confirmedSameFilm = false;
        var allDifferentFilm = true;
        foreach (var candidate in titleMatches)
        {
            candidate.ProviderIds.TryGetValue(ProviderIds.Imdb, out var candidateImdb);
            candidate.ProviderIds.TryGetValue(ProviderIds.Tmdb, out var candidateTmdb);

            if (string.IsNullOrEmpty(candidateImdb) || string.Equals(candidateTmdb, gapTmdb, StringComparison.Ordinal))
            {
                // No IMDb id to compare, or it already carries the gap's id: cannot call it a different film.
                allDifferentFilm = false;
                continue;
            }

            if (string.Equals(candidateImdb, gapImdb, StringComparison.OrdinalIgnoreCase))
            {
                candidate.Note = "same film, confirmed by a matching IMDb id (owned under the wrong TheMovieDb id)";
                confirmedSameFilm = true;
                allDifferentFilm = false;
            }
            else
            {
                candidate.Note = "a different film that shares this title (its IMDb id differs)";
            }
        }

        var noun = gap.TargetKind == BaseItemKind.Series ? "show" : "movie";
        if (confirmedSameFilm)
        {
            diagnosis.Reason = DiagnosisReason.OwnedUnderWrongId;
            diagnosis.Summary = string.Create(CultureInfo.InvariantCulture, $"Confirmed: you own this {noun} under a different TheMovieDb id (its IMDb id matches). Fix the owned item's id and rescan.");
        }
        else if (allDifferentFilm && !hasSharedIdMatch && diagnosis.Reason == DiagnosisReason.OwnedUnderWrongId)
        {
            diagnosis.Reason = DiagnosisReason.NotOwned;
            diagnosis.Summary = string.Create(CultureInfo.InvariantCulture, $"No owned {noun} matches once external ids are compared: the same-title items you own are different films (their IMDb ids differ), so this looks like a genuine gap.");
        }
    }

    // The kinds the diagnosis can analyze: movies and shows (TheMovieDb-keyed), albums (MusicBrainz
    // release-group), and books (OpenLibrary work).
    public static bool IsDiagnosable(BaseItemKind kind)
        => kind is BaseItemKind.Movie or BaseItemKind.Series or BaseItemKind.MusicAlbum or BaseItemKind.Book;

    // An item's external ids as a case-insensitive map (blanks dropped), mirroring GapItem.ProviderIds so
    // the diagnosis stays provider-agnostic and ProviderLinks covers whatever ids the item carries. Shared
    // with GapDiagnostics.DiagnoseSeriesContent, which reads the same shape off the owning series.
    public static IReadOnlyDictionary<string, string> ProviderIdsOf(BaseItem item)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in item.ProviderIds)
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                map[pair.Key] = pair.Value;
            }
        }

        return map;
    }

    private static GapDiagnosis Evaluate(GapItem gap, OwnedIndex index)
    {
        var kind = gap.TargetKind;
        var primaryLabel = PrimaryProviderLabel(kind);
        gap.ProviderIds.TryGetValue(PrimaryProvider(kind), out var gapPrimary);
        gap.ProviderIds.TryGetValue(ProviderIds.Imdb, out var gapImdb);
        gap.ProviderIds.TryGetValue(ProviderIds.Tvdb, out var gapTvdb);
        var wantName = TextKey.Normalize(gap.Name);

        var target = new DiagnosisItem
        {
            Relation = "target",
            Name = gap.Name,
            Year = gap.Year,
            ProviderIds = gap.ProviderIds,
            Note = "reported missing",
            Links = ProviderLinks.Build(kind, gap.ProviderIds)
        };

        var noun = Noun(kind);
        var candidates = new List<DiagnosisItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var titleMismatch = false;
        var titleStale = false;
        var unreleasedLookalike = false;

        if (index.ByTitle.TryGetValue((kind, wantName), out var titleHits))
        {
            // Honor name + year before falling back to name alone. Prefer an exact-year match: when the gap
            // has a year and an owned item shares it exactly, treat that as the match and ignore same-title
            // owned items whose year differs (even by one), since those are a different release sharing the
            // title (The Game 1997 the thriller vs The Game 1998 the comedy). The one-year tolerance only
            // applies as a fallback (release-date jitter) when nothing matches the year exactly.
            var hasExactYear = gap.Year.HasValue && titleHits.Any(o => o.Year == gap.Year);
            foreach (var owned in titleHits)
            {
                // An owned item is on disk, so it came out; an upcoming gap has not come out yet. They cannot
                // be the same work however exactly the titles line up, and the year test alone does not catch
                // it because an announced title usually carries no date at all (the announced "Highlander"
                // against the 1986 one you own). Listed rather than skipped: the reader can see the same title
                // in their library and is owed the reason it is not the match. A shared external id outranks
                // this, since that is evidence of identity rather than of a coincidental title.
                if (gap.IsUpcoming && owned.Year.HasValue && !SharesAnyId(owned, gap.ProviderIds, PrimaryProvider(kind)))
                {
                    if (seen.Add(owned.JellyfinId))
                    {
                        unreleasedLookalike = true;
                        candidates.Add(ToItem(owned, "otherRelease", string.Create(CultureInfo.InvariantCulture, $"same title, but this gap is not out yet, so the {owned.Year} {noun} you own is a different release")));
                    }

                    continue;
                }

                // A same-title owned item more than a year off is always a different release (a remake), so
                // skip it. A year missing on either side cannot rule it out, so it still matches on name (this
                // stops owning "Ocean's Eleven" 2001 from flagging the missing 1960 original as a mismatch).
                if (YearConflicts(gap.Year, owned.Year))
                {
                    continue;
                }

                // An exact-year match exists, so a same-title item that is only a year off is a different film.
                if (hasExactYear && owned.Year.HasValue && owned.Year != gap.Year)
                {
                    continue;
                }

                if (!seen.Add(owned.JellyfinId))
                {
                    continue;
                }

                var ownedPrimary = PrimaryId(owned);
                string note;
                if (ownedPrimary is null)
                {
                    note = string.Create(CultureInfo.InvariantCulture, $"same title, no {primaryLabel} id");
                    titleMismatch = true;
                }
                else if (string.Equals(ownedPrimary, gapPrimary, StringComparison.Ordinal))
                {
                    note = "same title and id (this gap may be stale)";
                    titleStale = true;
                }
                else
                {
                    note = "same title, different id (probably misidentified)";
                    titleMismatch = true;
                }

                candidates.Add(ToItem(owned, "titleMatch", note));
            }
        }

        var idHolderMismatch = false;
        if (!string.IsNullOrEmpty(gapPrimary) && index.ByPrimaryId.TryGetValue((kind, gapPrimary), out var idHits))
        {
            foreach (var owned in idHits)
            {
                if (!seen.Add(owned.JellyfinId))
                {
                    continue;
                }

                idHolderMismatch = true;
                candidates.Add(ToItem(owned, "idHolder", "carries this id but a different title (probably misidentified)"));
            }
        }

        // B: corroborate by a secondary id. An owned item that shares the gap's IMDb or TheTVDB id but not
        // its TheMovieDb id is owned under the wrong TheMovieDb id, even when its title was localized and so
        // did not match above.
        foreach (var (provider, label, gapId) in new[] { (ProviderIds.Imdb, "IMDb", gapImdb), (ProviderIds.Tvdb, "TheTVDB", gapTvdb) })
        {
            if (string.IsNullOrEmpty(gapId) || !index.BySecondaryId.TryGetValue((kind, provider, gapId), out var idMatches))
            {
                continue;
            }

            foreach (var owned in idMatches)
            {
                if (!seen.Add(owned.JellyfinId))
                {
                    continue;
                }

                titleMismatch = true;
                candidates.Add(ToItem(owned, "idMatch", string.Create(CultureInfo.InvariantCulture, $"matched by {label} id; TheMovieDb id differs or is missing")));
            }
        }

        // C1: a wrong-class id on the gap itself (a typed-provider check) means the match never had a chance.
        var wrongClass = WrongClassId(gap.ProviderIds);

        string summary;
        DiagnosisReason reason;
        if (titleMismatch)
        {
            reason = DiagnosisReason.OwnedUnderWrongId;
            summary = string.Create(CultureInfo.InvariantCulture, $"Likely a metadata mismatch: you appear to own this {noun} already, under a different or missing {primaryLabel} id. Compare the ids below, fix the owned item, and rescan.");
        }
        else if (idHolderMismatch)
        {
            reason = DiagnosisReason.CarriesAnothersId;
            summary = "An owned item carries this title's id but looks like a different title. Check the identification of the item below.";
        }
        else if (titleStale)
        {
            reason = DiagnosisReason.Stale;
            summary = "An owned item already has this exact title and id, so this gap looks stale. A rescan should clear it.";
        }
        else if (wrongClass is not null)
        {
            reason = DiagnosisReason.WrongIdClass;
            summary = string.Create(CultureInfo.InvariantCulture, $"This gap cannot match because {wrongClass}. Fix that id and rescan.");
        }
        else if (unreleasedLookalike)
        {
            reason = DiagnosisReason.NotOwned;
            summary = string.Create(CultureInfo.InvariantCulture, $"This {noun} is not out yet, so the same-titled {noun} you own is an earlier, different release. Nothing is misidentified: leave the owned item alone.");
        }
        else
        {
            reason = DiagnosisReason.NotOwned;
            summary = string.Create(CultureInfo.InvariantCulture, $"No owned {noun} matches this by title, so it looks like a genuine gap: you do not own it.");
        }

        return new GapDiagnosis
        {
            GapId = gap.Id,
            Summary = summary,
            Reason = reason,
            TargetKind = kind,
            Target = target,
            Candidates = candidates
        };
    }

    private static OwnedIndex BuildIndex(IReadOnlyList<BaseItem> owned)
    {
        var index = new OwnedIndex();
        foreach (var item in owned)
        {
            var entry = new OwnedItem(
                item.GetBaseItemKind(),
                item.Name ?? string.Empty,
                TextKey.Normalize(item.Name),
                item.ProductionYear,
                ProviderIdsOf(item),
                item.Id.ToString("N", CultureInfo.InvariantCulture));

            index.All.Add(entry);
            Add(index.ByTitle, (entry.Kind, entry.NormalizedName), entry);
            var primary = PrimaryId(entry);
            if (primary is not null)
            {
                Add(index.ByPrimaryId, (entry.Kind, primary), entry);
            }

            foreach (var provider in SecondaryIdProviders)
            {
                if (entry.ProviderIds.TryGetValue(provider, out var secondary))
                {
                    Add(index.BySecondaryId, (entry.Kind, provider, secondary), entry);
                }
            }
        }

        return index;
    }

    private static DiagnosisItem ToItem(OwnedItem owned, string relation, string? note) => new()
    {
        Relation = relation,
        Name = owned.Name,
        Year = owned.Year,
        ProviderIds = owned.ProviderIds,
        JellyfinItemId = owned.JellyfinId,
        Note = note,
        Links = ProviderLinks.Build(owned.Kind, owned.ProviderIds)
    };

    // The provider an item of this kind is keyed on for the id match and the ownership diff.
    private static string PrimaryProvider(BaseItemKind kind) => kind switch
    {
        BaseItemKind.MusicAlbum => ProviderIds.MusicBrainzReleaseGroup,
        BaseItemKind.Book => ProviderIds.OpenLibrary,
        _ => ProviderIds.Tmdb
    };

    // The display name of the primary provider, for the diagnosis messages.
    private static string PrimaryProviderLabel(BaseItemKind kind) => kind switch
    {
        BaseItemKind.MusicAlbum => "MusicBrainz",
        BaseItemKind.Book => ProviderIds.OpenLibrary,
        _ => "TheMovieDb"
    };

    // The noun for this kind, for the diagnosis messages.
    private static string Noun(BaseItemKind kind) => kind switch
    {
        BaseItemKind.Series => "show",
        BaseItemKind.MusicAlbum => "album",
        BaseItemKind.Book => "book",
        BaseItemKind.Movie => "movie",
        _ => "item"
    };

    // The primary id the matching indexes on (TheMovieDb for movies/shows, MusicBrainz release-group for
    // albums, OpenLibrary work for books); null when absent.
    private static string? PrimaryId(OwnedItem owned)
        => owned.ProviderIds.TryGetValue(PrimaryProvider(owned.Kind), out var id) ? id : null;

    // A wrong-class id does not fit its provider slot. Only typed-id providers can be judged without a
    // network call: IMDb here (an "nm" person id where a "tt" title belongs). Numeric TheMovieDb/TheTVDB ids
    // are opaque, so that confirmation is left to the deeper (networked) pass. OpenLibrary keys ("...A"
    // author, "...W" work) join this once the Books diagnosis lands.
    private static string? WrongClassId(IReadOnlyDictionary<string, string> ids)
    {
        if (ids.TryGetValue(ProviderIds.Imdb, out var imdb) && imdb.StartsWith("nm", StringComparison.OrdinalIgnoreCase))
        {
            return "its IMDb id is a person id (nm...), not a title id (tt...)";
        }

        return null;
    }

    // Whether an owned item carries any of the gap's external ids: its kind's primary id, or one of the
    // secondary ones the diagnosis corroborates with. That is evidence of identity, as against a title two
    // unrelated releases happen to share.
    private static bool SharesAnyId(OwnedItem owned, IReadOnlyDictionary<string, string> gapIds, string primaryProvider)
    {
        foreach (var provider in SecondaryIdProviders.Append(primaryProvider))
        {
            if (gapIds.TryGetValue(provider, out var gapId)
                && !string.IsNullOrEmpty(gapId)
                && owned.ProviderIds.TryGetValue(provider, out var ownedId)
                && string.Equals(ownedId, gapId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Two known years more than a year apart mean a different release sharing the title (a remake), not the
    // same work under the wrong id. A year missing on either side cannot rule it out. The one-year slack
    // absorbs the usual release-date jitter between a catalog's year and the library's production year.
    private static bool YearConflicts(int? a, int? b)
        => a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) > 1;

    private static void Add<TKey>(Dictionary<TKey, List<OwnedItem>> map, TKey key, OwnedItem value)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<OwnedItem>();
            map[key] = list;
        }

        list.Add(value);
    }

    private readonly record struct OwnedItem(BaseItemKind Kind, string Name, string NormalizedName, int? Year, IReadOnlyDictionary<string, string> ProviderIds, string JellyfinId);

    private sealed class OwnedIndex
    {
        public List<OwnedItem> All { get; } = new();

        public Dictionary<(BaseItemKind Kind, string Name), List<OwnedItem>> ByTitle { get; } = new();

        // Owned items keyed by their primary id (TheMovieDb for movies/shows, MusicBrainz release-group for
        // albums, OpenLibrary work for books), for the id match and the audit's duplicate-id detection.
        public Dictionary<(BaseItemKind Kind, string Id), List<OwnedItem>> ByPrimaryId { get; } = new();

        // Owned items keyed by a secondary id (provider + value), for corroborating a gap whose title was
        // localized but whose IMDb/TheTVDB id still matches.
        public Dictionary<(BaseItemKind Kind, string Provider, string Id), List<OwnedItem>> BySecondaryId { get; } = new();
    }
}
