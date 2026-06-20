using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Services.Diagnostics;

/// <summary>
/// Explains why a movie or show gap is reported missing, and audits the library for the same kind of
/// identification problem in bulk. The common cause is a metadata mismatch: the library already holds the
/// title under a different (or absent) TheMovieDb id, so the ownership diff, which matches on provider id,
/// cannot see it. Library-only and synchronous: it builds a one-time index of owned movies and shows and
/// reads from that, so a per-gap diagnosis is one library load and a whole-library audit is still one.
/// </summary>
public sealed class GapDiagnostics
{
    // The secondary ids the diagnosis corroborates a gap against (the primary key stays TheMovieDb).
    private static readonly string[] SecondaryIdProviders = { "Imdb", "Tvdb" };

    private readonly ILibraryManager _libraryManager;
    private readonly TmdbClient _tmdb;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapDiagnostics"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="tmdb">The TheMovieDb client, for the deeper (networked) pass.</param>
    public GapDiagnostics(ILibraryManager libraryManager, TmdbClient tmdb)
    {
        _libraryManager = libraryManager;
        _tmdb = tmdb;
    }

    /// <summary>
    /// Diagnoses a single gap, returning a verdict, the gap itself, and the owned candidate items. Library
    /// only by default; when <paramref name="deeper"/> is set it also confirms against TheMovieDb, filling
    /// the gap's (and each candidate's) IMDb/TheTVDB ids so a cross-id match can fire even when an item
    /// carried only a TheMovieDb id locally (one networked pass, behind the dashboard's "Deeper analysis").
    /// </summary>
    /// <param name="gap">The gap to diagnose.</param>
    /// <param name="deeper">When true, run the extra TheMovieDb confirmation pass.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The diagnosis.</returns>
    public async Task<GapDiagnosis> DiagnoseAsync(GapItem gap, bool deeper, CancellationToken cancellationToken)
    {
        var owned = LoadOwned(gap.TargetKind);
        var isSeries = gap.TargetKind == BaseItemKind.Series;

        if (deeper)
        {
            // Resolve the gap's external ids from the source provider first, so the cross-id match sees them
            // even when the gap carried only a TheMovieDb id locally.
            var enriched = new Dictionary<string, string>(gap.ProviderIds, StringComparer.OrdinalIgnoreCase);
            await ResolveExternalIdsAsync(enriched, isSeries, cancellationToken).ConfigureAwait(false);
            gap = new GapItem
            {
                Id = gap.Id,
                TargetKind = gap.TargetKind,
                Name = gap.Name,
                Year = gap.Year,
                ProviderIds = enriched
            };
        }

        var diagnosis = DiagnoseAgainst(gap, owned);

        if (deeper)
        {
            // The candidates, which come from owned items' local ids, need extra per-row resolution.
            foreach (var candidate in diagnosis.Candidates)
            {
                var ids = new Dictionary<string, string>(candidate.ProviderIds, StringComparer.OrdinalIgnoreCase);
                if (await ResolveExternalIdsAsync(ids, isSeries, cancellationToken).ConfigureAwait(false))
                {
                    candidate.ProviderIds = ids;
                    candidate.Links = ProviderLinks.Build(gap.TargetKind, ids);
                }
            }

            diagnosis.Deepened = true;
        }

        return diagnosis;
    }

    // Diagnose a gap against an explicit set of owned items: the testable seam, no library load. The public
    // entry supplies the owned movies/shows; tests supply their own.
    internal static GapDiagnosis DiagnoseAgainst(GapItem gap, IReadOnlyList<BaseItem> owned)
    {
        if (gap.TargetKind is not (BaseItemKind.Movie or BaseItemKind.Series))
        {
            return new GapDiagnosis { Summary = "Identification diagnosis is available for movie and show gaps only." };
        }

        return Evaluate(gap, BuildIndex(owned));
    }

    // Resolve a TheMovieDb id to its IMDb/TheTVDB ids and fill any that are missing. Returns true when it
    // added something. A no-op (false) when both are already present or there is no numeric TheMovieDb id.
    private async Task<bool> ResolveExternalIdsAsync(Dictionary<string, string> ids, bool isSeries, CancellationToken cancellationToken)
    {
        // A movie only ever resolves an IMDb id from TheMovieDb (no TheTVDB), so it is complete with IMDb
        // alone; a series needs both. Skipping when nothing more can be added avoids a wasted lookup (and the
        // cache miss it would cost) for the common movie-with-IMDb case.
        var haveAll = ids.ContainsKey("Imdb") && (!isSeries || ids.ContainsKey("Tvdb"));
        if (haveAll
            || !ids.TryGetValue("Tmdb", out var tmdb)
            || !int.TryParse(tmdb, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
        {
            return false;
        }

        var (imdb, tvdb) = await _tmdb.GetExternalIdsAsync(tmdbId, isSeries, cancellationToken).ConfigureAwait(false);
        var added = false;
        if (!string.IsNullOrEmpty(imdb) && !ids.ContainsKey("Imdb"))
        {
            ids["Imdb"] = imdb;
            added = true;
        }

        if (!string.IsNullOrEmpty(tvdb) && !ids.ContainsKey("Tvdb"))
        {
            ids["Tvdb"] = tvdb;
            added = true;
        }

        return added;
    }

    /// <summary>
    /// Audits the library for identification problems: gaps that look like a metadata mismatch (you own
    /// them under a different id), and owned items that share a provider id (so one is misidentified).
    /// </summary>
    /// <param name="report">The current gap report (its gaps are checked; its scan time stamps the audit).</param>
    /// <returns>The audit.</returns>
    public IdentificationAudit BuildAudit(GapReport report)
        => AuditAgainst(report, LoadOwned(BaseItemKind.Movie, BaseItemKind.Series));

    // Audit a report against an explicit set of owned items: the testable seam, no library load.
    internal static IdentificationAudit AuditAgainst(GapReport report, IReadOnlyList<BaseItem> owned)
    {
        var index = BuildIndex(owned);

        var mismatches = new List<GapDiagnosis>();
        var checkedCount = 0;
        foreach (var gap in report.Items)
        {
            if (gap.TargetKind is not (BaseItemKind.Movie or BaseItemKind.Series))
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

        var duplicates = new List<DuplicateIdGroup>();
        foreach (var pair in index.ByTmdb)
        {
            if (pair.Value.Count < 2)
            {
                continue;
            }

            duplicates.Add(new DuplicateIdGroup
            {
                Provider = "Tmdb",
                Id = pair.Key.Id,
                TargetKind = pair.Key.Kind,
                Items = pair.Value.Select(o => ToItem(o, "owned", null)).ToList()
            });
        }

        return new IdentificationAudit
        {
            // The audit does no fresh discovery; it analyses the report, so it carries the report's scan time.
            GeneratedUtc = report.GeneratedUtc,
            OwnedMovies = index.All.Count(o => o.Kind == BaseItemKind.Movie),
            OwnedShows = index.All.Count(o => o.Kind == BaseItemKind.Series),
            GapsChecked = checkedCount,
            Mismatches = mismatches,
            Duplicates = duplicates
        };
    }

    private static GapDiagnosis Evaluate(GapItem gap, OwnedIndex index)
    {
        gap.ProviderIds.TryGetValue("Tmdb", out var gapTmdb);
        gap.ProviderIds.TryGetValue("Imdb", out var gapImdb);
        gap.ProviderIds.TryGetValue("Tvdb", out var gapTvdb);
        var kind = gap.TargetKind;
        var wantName = Normalize(gap.Name);

        var target = new DiagnosisItem
        {
            Relation = "target",
            Name = gap.Name,
            Year = gap.Year,
            ProviderIds = gap.ProviderIds,
            Note = "reported missing",
            Links = ProviderLinks.Build(kind, gap.ProviderIds)
        };

        var candidates = new List<DiagnosisItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var titleMismatch = false;
        var titleStale = false;

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

                var ownedTmdb = Tmdb(owned);
                string note;
                if (ownedTmdb is null)
                {
                    note = "same title, no TheMovieDb id";
                    titleMismatch = true;
                }
                else if (string.Equals(ownedTmdb, gapTmdb, StringComparison.Ordinal))
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
        if (!string.IsNullOrEmpty(gapTmdb) && index.ByTmdb.TryGetValue((kind, gapTmdb), out var idHits))
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
        foreach (var (provider, label, gapId) in new[] { ("Imdb", "IMDb", gapImdb), ("Tvdb", "TheTVDB", gapTvdb) })
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

        var noun = kind == BaseItemKind.Series ? "show" : "movie";
        string summary;
        DiagnosisReason reason;
        if (titleMismatch)
        {
            reason = DiagnosisReason.OwnedUnderWrongId;
            summary = string.Create(CultureInfo.InvariantCulture, $"Likely a metadata mismatch: you appear to own this {noun} already, under a different or missing TheMovieDb id. Compare the ids below, fix the owned item, and rescan.");
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

    private IReadOnlyList<BaseItem> LoadOwned(params BaseItemKind[] kinds)
    {
        // Only movies and shows are diagnosable, so skip the load entirely for any other kind
        if (kinds.Any(k => k is not (BaseItemKind.Movie or BaseItemKind.Series)))
        {
            return [];
        }

        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = kinds,
            Recursive = true,

            // Owned means a real file: exclude virtual placeholders (e.g. minted items, missing entries) so
            // the diagnosis diffs against what is actually in the library.
            IsVirtualItem = false
        });
    }

    private static OwnedIndex BuildIndex(IReadOnlyList<BaseItem> owned)
    {
        var index = new OwnedIndex();
        foreach (var item in owned)
        {
            var entry = new OwnedItem(
                item.GetBaseItemKind(),
                item.Name ?? string.Empty,
                Normalize(item.Name),
                item.ProductionYear,
                ProviderIdsOf(item),
                item.Id.ToString("N", CultureInfo.InvariantCulture));

            index.All.Add(entry);
            Add(index.ByTitle, (entry.Kind, entry.NormalizedName), entry);
            var tmdb = Tmdb(entry);
            if (tmdb is not null)
            {
                Add(index.ByTmdb, (entry.Kind, tmdb), entry);
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

    // An item's external ids as a case-insensitive map (blanks dropped), mirroring GapItem.ProviderIds so
    // the diagnosis stays provider-agnostic and ProviderLinks covers whatever ids the item carries.
    private static IReadOnlyDictionary<string, string> ProviderIdsOf(BaseItem item)
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

    // The TheMovieDb id, still the one key the matching indexes on (movie/show diagnosis); null when absent.
    private static string? Tmdb(OwnedItem owned) => owned.ProviderIds.TryGetValue("Tmdb", out var id) ? id : null;

    // A wrong-class id does not fit its provider slot. Only typed-id providers can be judged without a
    // network call: IMDb here (an "nm" person id where a "tt" title belongs). Numeric TheMovieDb/TheTVDB ids
    // are opaque, so that confirmation is left to the deeper (networked) pass. OpenLibrary keys ("...A"
    // author, "...W" work) join this once the Books diagnosis lands.
    private static string? WrongClassId(IReadOnlyDictionary<string, string> ids)
    {
        if (ids.TryGetValue("Imdb", out var imdb) && imdb.StartsWith("nm", StringComparison.OrdinalIgnoreCase))
        {
            return "its IMDb id is a person id (nm...), not a title id (tt...)";
        }

        return null;
    }

    // Two known years more than a year apart mean a different release sharing the title (a remake), not the
    // same work under the wrong id. A year missing on either side cannot rule it out. The one-year slack
    // absorbs the usual release-date jitter between a catalogue's year and the library's production year.
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

    // Lowercase, letters and digits only, so punctuation, spacing, and case differences do not block a
    // match. The year is kept out of this key and compared separately (see YearConflicts) so a remake that
    // shares a title is told apart by its year rather than being folded in here.
    private static string Normalize(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private readonly record struct OwnedItem(BaseItemKind Kind, string Name, string NormalizedName, int? Year, IReadOnlyDictionary<string, string> ProviderIds, string JellyfinId);

    private sealed class OwnedIndex
    {
        public List<OwnedItem> All { get; } = new();

        public Dictionary<(BaseItemKind Kind, string Name), List<OwnedItem>> ByTitle { get; } = new();

        public Dictionary<(BaseItemKind Kind, string Id), List<OwnedItem>> ByTmdb { get; } = new();

        // Owned items keyed by a secondary id (provider + value), for corroborating a gap whose title was
        // localized but whose IMDb/TheTVDB id still matches.
        public Dictionary<(BaseItemKind Kind, string Provider, string Id), List<OwnedItem>> BySecondaryId { get; } = new();
    }
}
