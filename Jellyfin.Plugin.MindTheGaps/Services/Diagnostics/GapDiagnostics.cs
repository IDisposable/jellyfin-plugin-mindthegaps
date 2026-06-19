using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
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
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapDiagnostics"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    public GapDiagnostics(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Diagnoses a single gap, returning a verdict, the gap itself, and the owned candidate items.
    /// </summary>
    /// <param name="gap">The gap to diagnose.</param>
    /// <returns>The diagnosis.</returns>
    public GapDiagnosis Diagnose(GapItem gap)
    {
        if (gap.TargetKind is not (BaseItemKind.Movie or BaseItemKind.Series))
        {
            return new GapDiagnosis { Summary = "Identification diagnosis is available for movie and show gaps only." };
        }

        return Evaluate(gap, BuildIndex(new[] { gap.TargetKind }));
    }

    /// <summary>
    /// Audits the library for identification problems: gaps that look like a metadata mismatch (you own
    /// them under a different id), and owned items that share a provider id (so one is misidentified).
    /// </summary>
    /// <param name="report">The current gap report (its gaps are checked; its scan time stamps the audit).</param>
    /// <returns>The audit.</returns>
    public IdentificationAudit BuildAudit(GapReport report)
    {
        var index = BuildIndex(new[] { BaseItemKind.Movie, BaseItemKind.Series });

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
            gap.ProviderIds.TryGetValue("Tmdb", out var gapTmdb);
            var isMismatch = diagnosis.Candidates.Any(c =>
                (string.Equals(c.Relation, "titleMatch", StringComparison.Ordinal) && !string.Equals(c.Tmdb, gapTmdb, StringComparison.Ordinal))
                || string.Equals(c.Relation, "idHolder", StringComparison.Ordinal));
            if (isMismatch)
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

    private GapDiagnosis Evaluate(GapItem gap, OwnedIndex index)
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
            Tmdb = gapTmdb,
            Imdb = gapImdb,
            Tvdb = gapTvdb,
            Note = "reported missing"
        };

        var candidates = new List<DiagnosisItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var titleMismatch = false;
        var titleStale = false;

        if (index.ByTitle.TryGetValue((kind, wantName), out var titleHits))
        {
            foreach (var owned in titleHits)
            {
                if (!seen.Add(owned.JellyfinId))
                {
                    continue;
                }

                var sameId = owned.Tmdb is not null && string.Equals(owned.Tmdb, gapTmdb, StringComparison.Ordinal);
                string note;
                if (owned.Tmdb is null)
                {
                    note = "same title, no TheMovieDb id";
                    titleMismatch = true;
                }
                else if (sameId)
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

        var noun = kind == BaseItemKind.Series ? "show" : "movie";
        string summary;
        if (titleMismatch)
        {
            summary = string.Create(CultureInfo.InvariantCulture, $"Likely a metadata mismatch: you appear to own this {noun} already, under a different or missing TheMovieDb id. Compare the ids below, fix the owned item, and rescan.");
        }
        else if (idHolderMismatch)
        {
            summary = "An owned item carries this title's id but looks like a different title. Check the identification of the item below.";
        }
        else if (titleStale)
        {
            summary = "An owned item already has this exact title and id, so this gap looks stale. A rescan should clear it.";
        }
        else
        {
            summary = string.Create(CultureInfo.InvariantCulture, $"No owned {noun} matches this by title, so it looks like a genuine gap: you do not own it.");
        }

        return new GapDiagnosis
        {
            Summary = summary,
            TargetKind = kind,
            Target = target,
            Candidates = candidates
        };
    }

    private OwnedIndex BuildIndex(IReadOnlyList<BaseItemKind> kinds)
    {
        var index = new OwnedIndex();
        var owned = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = kinds.ToArray(),
            Recursive = true
        });

        foreach (var item in owned)
        {
            item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdb);
            item.TryGetProviderId(MetadataProvider.Imdb, out var imdb);
            item.TryGetProviderId(MetadataProvider.Tvdb, out var tvdb);

            var entry = new OwnedItem(
                item.GetBaseItemKind(),
                item.Name ?? string.Empty,
                Normalize(item.Name),
                item.ProductionYear,
                Blank(tmdb),
                Blank(imdb),
                Blank(tvdb),
                item.Id.ToString("N", CultureInfo.InvariantCulture));

            index.All.Add(entry);
            Add(index.ByTitle, (entry.Kind, entry.NormalizedName), entry);
            if (entry.Tmdb is not null)
            {
                Add(index.ByTmdb, (entry.Kind, entry.Tmdb), entry);
            }
        }

        return index;
    }

    private static DiagnosisItem ToItem(OwnedItem owned, string relation, string? note) => new()
    {
        Relation = relation,
        Name = owned.Name,
        Year = owned.Year,
        Tmdb = owned.Tmdb,
        Imdb = owned.Imdb,
        Tvdb = owned.Tvdb,
        JellyfinItemId = owned.JellyfinId,
        Note = note
    };

    private static string? Blank(string? value) => string.IsNullOrEmpty(value) ? null : value;

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
    // match (and a mistagged item's wrong year does not hide it).
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

    private readonly record struct OwnedItem(BaseItemKind Kind, string Name, string NormalizedName, int? Year, string? Tmdb, string? Imdb, string? Tvdb, string JellyfinId);

    private sealed class OwnedIndex
    {
        public List<OwnedItem> All { get; } = new();

        public Dictionary<(BaseItemKind Kind, string Name), List<OwnedItem>> ByTitle { get; } = new();

        public Dictionary<(BaseItemKind Kind, string Id), List<OwnedItem>> ByTmdb { get; } = new();
    }
}
