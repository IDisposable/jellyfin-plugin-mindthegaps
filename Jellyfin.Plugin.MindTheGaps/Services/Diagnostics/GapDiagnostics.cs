using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
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
        // Episode and season gaps are diagnosed differently: rather than matching an owned item by id, ask
        // whether the missing content belongs to the series you own at all, or to a same-named reboot the
        // owning item is mis-tagged as (the "V 1984 versus V 2009" case). Library only, no networked pass.
        if (gap.TargetKind is BaseItemKind.Episode or BaseItemKind.Season)
        {
            return DiagnoseSeriesContent(gap);
        }

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

            // Now that both sides carry resolved IMDb ids, use them to tell a real misidentification (same
            // film under the wrong TheMovieDb id) from a coincidental title clash (a different film sharing
            // the title), which a library-only, title-keyed match cannot distinguish.
            ApplyCrossProviderDisagreement(gap, diagnosis);

            diagnosis.Deepened = true;
        }

        return diagnosis;
    }

    // Diagnose a gap against an explicit set of owned items: the testable seam, no library load. The public
    // entry supplies the owned movies/shows; tests supply their own.
    internal static GapDiagnosis DiagnoseAgainst(GapItem gap, IReadOnlyList<BaseItem> owned)
    {
        if (!IsDiagnosable(gap.TargetKind))
        {
            return new GapDiagnosis { Summary = "Identification diagnosis is available for movie, show, album, and book gaps only." };
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

        // The audit's owned set is movies and shows only (the guard above), so every primary id here is a
        // TheMovieDb id; the duplicate section stays TheMovieDb-specific.
        var duplicates = new List<DuplicateIdGroup>();
        foreach (var pair in index.ByPrimaryId)
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

    // Diagnose an episode or season gap against the owning series, the episode numbers you own for it, and the
    // years of the episodes you own (and are missing): the testable seam, no library load. An episode whose own
    // number is among the owned set is not missing at all (a stale gap, or a numbering the cross-check disagrees
    // on); the year heuristic cannot see that, so the owned numbers are checked first. For the rest, the owned
    // run expanded through the missing years into the series' episode era tells a genuine missing piece (within
    // that era) from content of a different same-named series (a reboot) the owning item is mis-tagged as. This
    // is the same era the library scan uses, so the popup and the report agree. The series' ids ride along as an
    // extra disambiguation the reader can check; the verdict does not depend on them.
    internal static GapDiagnosis DiagnoseSeriesContentAgainst(
        GapItem gap,
        string? seriesName,
        int? seriesYear,
        IReadOnlyDictionary<string, string> seriesProviderIds,
        string? seriesJellyfinId,
        IReadOnlyList<int> ownedEpisodeYears,
        IReadOnlyList<int> missingEpisodeYears,
        IReadOnlyList<(int Season, int Number, string? Title, int Versions)> ownedEpisodes)
    {
        var name = string.IsNullOrEmpty(seriesName) ? "this series" : seriesName!;
        var noun = gap.TargetKind == BaseItemKind.Season ? "season" : "episode";

        var target = new DiagnosisItem
        {
            Relation = "target",
            Name = gap.Name,
            Year = gap.Year,
            ProviderIds = gap.ProviderIds,
            Note = "reported missing",
            Links = ProviderLinks.Build(gap.TargetKind, gap.ProviderIds)
        };

        var candidates = new List<DiagnosisItem>();
        if (!string.IsNullOrEmpty(seriesJellyfinId) || seriesProviderIds.Count > 0)
        {
            candidates.Add(new DiagnosisItem
            {
                Relation = "series",
                Name = name,
                Year = seriesYear,
                ProviderIds = seriesProviderIds,
                JellyfinItemId = seriesJellyfinId,
                Note = "the owning series",
                Links = ProviderLinks.Build(BaseItemKind.Series, seriesProviderIds)
            });
        }

        GapDiagnosis Result(DiagnosisReason reason, string summary) => new()
        {
            GapId = gap.Id,
            Summary = summary,
            Reason = reason,
            TargetKind = gap.TargetKind,
            Target = target,
            Candidates = candidates
        };

        // Identity check first: an episode whose own number is among the ones the library owns for this series
        // is not missing at all. The year comparison below cannot tell that apart from a genuine gap, so probe
        // the owned numbers directly. Only an episode gap carries a parseable season/number; a season gap falls
        // through to the year logic.
        string? episodeCode = null;
        if (gap.TargetKind == BaseItemKind.Episode && SeriesGapKey.TryParseEpisode(gap.Id, out var season, out var number))
        {
            episodeCode = string.Create(CultureInfo.InvariantCulture, $"S{season:D2}E{number:D2}");

            // The exact number is owned, so it is not missing (a stale gap, or a numbering the cross-check
            // disagrees on).
            if (ownedEpisodes.Any(e => e.Season == season && e.Number == number))
            {
                return Result(
                    DiagnosisReason.OwnedUnderWrongId,
                    string.Create(CultureInfo.InvariantCulture, $"{episodeCode} is among the episodes you own for '{name}', so it is not actually missing. The gap is most likely stale (rescan to clear it), or the cross-check source numbers this episode differently than your library."));
            }

            // The number is absent, but an episode with the same title (ignoring a part marker like "(2)" or
            // "Part 2") is owned at another number in the season: the content is present and the library numbers
            // it differently than the catalogue (a two-part episode, or the pilot counted as one episode here and
            // two there), so this is a false gap rather than a missing one.
            var titleKey = EpisodeTitleKey.Of(EpisodeTitleOf(gap.Name));
            if (titleKey.Length > 0)
            {
                foreach (var owned in ownedEpisodes)
                {
                    if (owned.Season == season && owned.Number != number && EpisodeTitleKey.Of(owned.Title) == titleKey)
                    {
                        var ownedCode = string.Create(CultureInfo.InvariantCulture, $"S{owned.Season:D2}E{owned.Number:D2}");
                        var versions = owned.Versions > 1
                            ? string.Create(CultureInfo.InvariantCulture, $" (with {owned.Versions} versions)")
                            : string.Empty;
                        return Result(
                            DiagnosisReason.OwnedUnderWrongId,
                            string.Create(CultureInfo.InvariantCulture, $"{episodeCode} is not in your library by number, but you own an episode with the same title at {ownedCode}{versions}. Your library most likely numbers this episode differently than the catalogue (a two-part episode, or the pilot counted as one episode here and two there); renumber {ownedCode} to match, or this stays a permanent false gap."));
                    }
                }
            }
        }

        if (gap.Year is not int airedYear || ownedEpisodeYears.Count == 0)
        {
            return Result(DiagnosisReason.NotOwned, string.Create(CultureInfo.InvariantCulture, $"There is not enough dated content to compare, so this {noun} looks like a genuine gap in '{name}'."));
        }

        // Expand the owned run through the series' missing-episode years into its real episode era, the same
        // way the library scan does, so an earlier or later season that bridges in episode by episode reads as
        // genuine and only a far-separated same-named reboot is flagged.
        var era = EpisodeEra.Expand((ownedEpisodeYears.Min(), ownedEpisodeYears.Max()), missingEpisodeYears);
        if (!EpisodeEra.IsOutside(airedYear, era))
        {
            var summary = episodeCode is null
                ? string.Create(CultureInfo.InvariantCulture, $"This {noun} aired {airedYear}, within the run of '{name}' ({era.Min} to {era.Max}), so it looks like a genuine missing {noun}.")
                : string.Create(CultureInfo.InvariantCulture, $"{episodeCode} is not among the episodes you own for '{name}', and it aired {airedYear}, within the run ({era.Min} to {era.Max}), so it is a genuine missing {noun}.");
            return Result(DiagnosisReason.NotOwned, summary);
        }

        var idHint = seriesProviderIds.Count > 0
            ? string.Create(CultureInfo.InvariantCulture, $" The series carries {DescribeIds(seriesProviderIds)}; confirm it points to the {era.Min}-{era.Max} series, not a {airedYear} one.")
            : " The series carries no external id to confirm against.";

        return Result(
            DiagnosisReason.OwnedUnderWrongId,
            string.Create(CultureInfo.InvariantCulture, $"This {noun} aired {airedYear}, but the run of '{name}' spans {era.Min} to {era.Max} with nothing bridging to {airedYear}, so it is almost certainly a different, same-named series (a reboot).{idHint}"));
    }

    // The owning series' external ids in a readable form, for the episode/season verdict's id hint.
    private static string DescribeIds(IReadOnlyDictionary<string, string> ids)
    {
        var parts = new List<string>();
        foreach (var (provider, label) in new[] { ("Tmdb", "TheMovieDb"), ("Tvdb", "TheTVDB"), ("Imdb", "IMDb"), ("TVmaze", "TVmaze") })
        {
            if (ids.TryGetValue(provider, out var value) && !string.IsNullOrEmpty(value))
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{label} {value}"));
            }
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "no external ids";
    }

    // The bare episode title out of a series-content gap name, which the gap builds as "{series} {code} - {title}".
    private static string EpisodeTitleOf(string gapName)
    {
        var dash = gapName.IndexOf(" - ", StringComparison.Ordinal);
        return dash >= 0 ? gapName[(dash + 3)..] : gapName;
    }

    private static GapDiagnosis Evaluate(GapItem gap, OwnedIndex index)
    {
        var kind = gap.TargetKind;
        var primaryLabel = PrimaryProviderLabel(kind);
        gap.ProviderIds.TryGetValue(PrimaryProvider(kind), out var gapPrimary);
        gap.ProviderIds.TryGetValue("Imdb", out var gapImdb);
        gap.ProviderIds.TryGetValue("Tvdb", out var gapTvdb);
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

        var noun = Noun(kind);
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

    // In the deeper pass, compare the gap's resolved IMDb id with each same-title owned candidate's resolved
    // IMDb id. A match confirms the candidate is the same film under the wrong TheMovieDb id; a mismatch means
    // a different film that merely shares the title. When every same-title candidate is a different film (and
    // nothing matched by a shared id), the gap is genuinely missing after all. Does nothing without a gap IMDb
    // id to compare, so it is safe to call whenever the deeper pass ran.
    internal static void ApplyCrossProviderDisagreement(GapItem gap, GapDiagnosis diagnosis)
    {
        gap.ProviderIds.TryGetValue("Imdb", out var gapImdb);
        gap.ProviderIds.TryGetValue("Tmdb", out var gapTmdb);
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
            candidate.ProviderIds.TryGetValue("Imdb", out var candidateImdb);
            candidate.ProviderIds.TryGetValue("Tmdb", out var candidateTmdb);

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

    // Diagnose an episode or season gap: load the owning series and the years of the episodes you own for it,
    // then defer to the pure verdict. The public entry; the seam takes the owned years directly so tests do
    // not need a library.
    internal GapDiagnosis DiagnoseSeriesContent(GapItem gap)
    {
        if (Guid.TryParse(gap.SourceItemId, out var seriesId) && _libraryManager.GetItemById(seriesId) is { } series)
        {
            return DiagnoseSeriesContentAgainst(
                gap,
                series.Name,
                series.ProductionYear,
                ProviderIdsOf(series),
                series.Id.ToString("N", CultureInfo.InvariantCulture),
                OwnedEpisodeYears(seriesId),
                MissingEpisodeYears(seriesId),
                OwnedEpisodes(seriesId));
        }

        return DiagnoseSeriesContentAgainst(gap, gap.SourceItemName, null, new Dictionary<string, string>(), null, [], [], []);
    }

    // The air years of the episodes the library actually owns (on disk) for a series, for the era comparison.
    private IReadOnlyList<int> OwnedEpisodeYears(Guid seriesId)
    {
        var years = new List<int>();
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = new[] { seriesId },
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            if (item is Episode episode && episode.PremiereDate is { } aired)
            {
                years.Add(aired.Year);
            }
        }

        return years;
    }

    // The air years of the series' missing episodes, used to expand the owned run into its full episode era.
    // The library scan surfaces an earlier or later season that bridges in through these years, so the
    // diagnosis reads it the same way instead of judging a now-surfaced episode against the owned run alone.
    private IReadOnlyList<int> MissingEpisodeYears(Guid seriesId)
    {
        var years = new List<int>();
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = new[] { seriesId },
            IsMissing = true,
            Recursive = true
        }))
        {
            if (item is Episode episode && episode.PremiereDate is { } aired)
            {
                years.Add(aired.Year);
            }
        }

        return years;
    }

    // The episodes the library owns on disk for a series (their season/number, title, and how many media
    // versions the item carries), so the diagnosis can tell a genuinely missing episode from one already present
    // under the same number, or the same title at another number (a two-part or off-by-one numbering mismatch).
    // A multi-episode file counts for every number in its span, matching how the scan reads ownership.
    private IReadOnlyList<(int Season, int Number, string? Title, int Versions)> OwnedEpisodes(Guid seriesId)
    {
        var owned = new List<(int Season, int Number, string? Title, int Versions)>();
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            AncestorIds = new[] { seriesId },
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            if (item is Episode episode && episode.ParentIndexNumber is int s && episode.IndexNumber is int n)
            {
                var versions = 1 + (episode.LocalAlternateVersions?.Length ?? 0);
                var last = episode.IndexNumberEnd is int end && end > n ? end : n;
                for (var num = n; num <= last; num++)
                {
                    owned.Add((s, num, episode.Name, versions));
                }
            }
        }

        return owned;
    }

    private IReadOnlyList<BaseItem> LoadOwned(params BaseItemKind[] kinds)
    {
        // Skip the load entirely for any kind the diagnosis cannot analyse.
        if (kinds.Any(k => !IsDiagnosable(k)))
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

    // The kinds the diagnosis can analyse: movies and shows (TheMovieDb-keyed), albums (MusicBrainz
    // release-group), and books (OpenLibrary work).
    private static bool IsDiagnosable(BaseItemKind kind)
        => kind is BaseItemKind.Movie or BaseItemKind.Series or BaseItemKind.MusicAlbum or BaseItemKind.Book;

    // The provider an item of this kind is keyed on for the id match and the ownership diff.
    private static string PrimaryProvider(BaseItemKind kind) => kind switch
    {
        BaseItemKind.MusicAlbum => "MusicBrainzReleaseGroup",
        BaseItemKind.Book => "OpenLibrary",
        _ => "Tmdb"
    };

    // The display name of the primary provider, for the diagnosis messages.
    private static string PrimaryProviderLabel(BaseItemKind kind) => kind switch
    {
        BaseItemKind.MusicAlbum => "MusicBrainz",
        BaseItemKind.Book => "OpenLibrary",
        _ => "TheMovieDb"
    };

    // The noun for this kind, for the diagnosis messages.
    private static string Noun(BaseItemKind kind) => kind switch
    {
        BaseItemKind.Series => "show",
        BaseItemKind.MusicAlbum => "album",
        BaseItemKind.Book => "book",
        _ => "movie"
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
