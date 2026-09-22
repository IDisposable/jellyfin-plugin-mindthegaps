using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.MindTheGaps.Services.Diagnostics;

/// <summary>
/// Explains why a movie, show, album, or book gap is reported missing, and audits the library for the same
/// kind of identification problem in bulk. The common cause is a metadata mismatch: the library already holds
/// the title under a different (or absent) primary id, so the ownership diff, which matches on provider id,
/// cannot see it. Library-only and synchronous: it builds a one-time index of the owned items in the audited
/// domain and reads from that, so a per-gap diagnosis is one library load and a whole-library audit is one.
/// This is the thin, library/network-touching orchestrator; the actual verdicts are pure and standalone in
/// <see cref="TitleIdentityDiagnosis"/> (movies/shows/albums/books), <see cref="SeriesContentDiagnosis"/>
/// (episodes/seasons), and <see cref="DuplicateSeasonFinder"/> (the season-folder structural audit), each
/// unit-testable without a scan or a library, for the same reason <see cref="Gaps.StaleOwnerPruner"/> is.
/// </summary>
public sealed class GapDiagnostics
{
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
                ReleaseDate = gap.ReleaseDate,
                IsUpcoming = gap.IsUpcoming,
                Domain = gap.Domain,
                ProviderIds = enriched
            };
        }

        var diagnosis = TitleIdentityDiagnosis.DiagnoseAgainst(gap, owned);

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
            TitleIdentityDiagnosis.ApplyCrossProviderDisagreement(gap, diagnosis);

            diagnosis.Deepened = true;
        }

        return diagnosis;
    }

    // Resolve a TheMovieDb id to its IMDb/TheTVDB ids and fill any that are missing. Returns true when it
    // added something. A no-op (false) when both are already present or there is no numeric TheMovieDb id.
    private async Task<bool> ResolveExternalIdsAsync(Dictionary<string, string> ids, bool isSeries, CancellationToken cancellationToken)
    {
        // A movie only ever resolves an IMDb id from TheMovieDb (no TheTVDB), so it is complete with IMDb
        // alone; a series needs both. Skipping when nothing more can be added avoids a wasted lookup (and the
        // cache miss it would cost) for the common movie-with-IMDb case.
        var haveAll = ids.ContainsKey(ProviderIds.Imdb) && (!isSeries || ids.ContainsKey(ProviderIds.Tvdb));
        if (haveAll || !ids.TryGetProviderIdAsInt(ProviderIds.Tmdb, out var tmdbId))
        {
            return false;
        }

        var (imdb, tvdb) = await _tmdb.GetExternalIdsAsync(tmdbId, isSeries, cancellationToken).ConfigureAwait(false);
        var added = false;
        if (!string.IsNullOrEmpty(imdb) && !ids.ContainsKey(ProviderIds.Imdb))
        {
            ids[ProviderIds.Imdb] = imdb;
            added = true;
        }

        if (!string.IsNullOrEmpty(tvdb) && !ids.ContainsKey(ProviderIds.Tvdb))
        {
            ids[ProviderIds.Tvdb] = tvdb;
            added = true;
        }

        return added;
    }

    /// <summary>
    /// Audits the library for identification problems: gaps that look like a metadata mismatch (you own
    /// them under a different id), and owned items that share a provider id (so one is misidentified).
    /// Scoped to the requested domain and pattern, so an export from the Shows view is not full of movies.
    /// </summary>
    /// <param name="report">The current gap report (its gaps are checked; its scan time stamps the audit).</param>
    /// <param name="domain">The domain to scope to (for example "Shows"), or null for every auditable domain.</param>
    /// <param name="pattern">The gap pattern to scope to (for example "SetCompletion"), or null for all patterns.</param>
    /// <returns>The audit.</returns>
    public IdentificationAudit BuildAudit(GapReport report, string? domain = null, string? pattern = null)
    {
        var scopeDomain = Enum.TryParse<MediaDomain>(domain, out var d) ? (MediaDomain?)d : null;
        var scopePattern = Enum.TryParse<GapPattern>(pattern, out var p) ? (GapPattern?)p : null;

        // The owned set (and so the duplicate-id section) follows the scoped domain to the kinds the diagnosis
        // can identify: Movies to Movie, Shows to Series, Music to MusicAlbum, Books to Book, no scope to all
        // four. A domain the diagnosis cannot identify yet (MusicVideos) audits nothing.
        BaseItemKind[] kinds = scopeDomain switch
        {
            MediaDomain.Movies => [BaseItemKind.Movie],
            MediaDomain.Shows => [BaseItemKind.Series],
            MediaDomain.Music => [BaseItemKind.MusicAlbum],
            MediaDomain.Books => [BaseItemKind.Book],
            null => [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.MusicAlbum, BaseItemKind.Book],
            _ => []
        };

        // A domain the diagnosis cannot identify yet has no owned set and no findings; report the scope and an
        // empty result rather than loading the whole library.
        if (kinds.Length == 0)
        {
            return new IdentificationAudit
            {
                GeneratedUtc = report.GeneratedUtc,
                DomainName = scopeDomain?.ToString(),
                PatternName = scopePattern?.ToString()
            };
        }

        var owned = LoadOwned(kinds);
        var audit = TitleIdentityDiagnosis.AuditAgainst(report, owned, scopeDomain, scopePattern);

        // Duplicate season folders are a Shows-only structural problem (a "Season 1" and a "Season 01" both
        // mapping to season 1), so only look when Series are in scope. It reads each series' seasons from the
        // library, so it lives here rather than in the pure DuplicateSeasonFinder seam.
        if (kinds.Contains(BaseItemKind.Series))
        {
            audit.DuplicateSeasons = DuplicateSeasonFinder.FindDuplicateSeasons(OwnedSeasons(owned));
        }

        return audit;
    }

    // Diagnose an episode or season gap: load the owning series and the years of the episodes you own for it,
    // then defer to the pure verdict. The public entry; the seam takes the owned years directly so tests do
    // not need a library.
    private GapDiagnosis DiagnoseSeriesContent(GapItem gap)
    {
        if (Guid.TryParse(gap.SourceItemId, out var seriesId) && _libraryManager.GetItemById(seriesId) is { } series)
        {
            return SeriesContentDiagnosis.DiagnoseSeriesContentAgainst(
                gap,
                series.Name,
                series.ProductionYear,
                TitleIdentityDiagnosis.ProviderIdsOf(series),
                series.Id.ToString("N", CultureInfo.InvariantCulture),
                OwnedEpisodeYears(seriesId),
                MissingEpisodeYears(seriesId),
                OwnedEpisodes(seriesId));
        }

        return SeriesContentDiagnosis.DiagnoseSeriesContentAgainst(gap, gap.SourceItemName, null, new Dictionary<string, string>(), null, [], [], []);
    }

    // The air years of the episodes the library actually owns (on disk) for a series, for the era comparison.
    private IReadOnlyList<int> OwnedEpisodeYears(Guid seriesId)
    {
        var years = new List<int>();
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            DtoOptions = LibraryQueryOptions.Minimal(),
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
            DtoOptions = LibraryQueryOptions.Minimal(),
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
            DtoOptions = LibraryQueryOptions.Minimal(),
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

    // The non-virtual (folder-backed) seasons of each owned series, with each season's path and episode count,
    // so the audit can flag a season number that more than one folder claims. Only real folders are read
    // (IsVirtualItem = false), so a virtual placeholder season is never mistaken for a duplicate folder.
    private IReadOnlyList<DuplicateSeasonFinder.SeasonInfo> OwnedSeasons(IReadOnlyList<BaseItem> owned)
    {
        var seasons = new List<DuplicateSeasonFinder.SeasonInfo>();
        foreach (var item in owned)
        {
            if (item is not Series series)
            {
                continue;
            }

            var seriesId = series.Id.ToString("N", CultureInfo.InvariantCulture);
            foreach (var child in _libraryManager.GetItemList(new InternalItemsQuery
            {
                DtoOptions = LibraryQueryOptions.Minimal(),
                IncludeItemTypes = new[] { BaseItemKind.Season },
                AncestorIds = new[] { series.Id },
                IsVirtualItem = false,
                Recursive = true
            }))
            {
                if (child is not Season season)
                {
                    continue;
                }

                var episodes = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    DtoOptions = LibraryQueryOptions.Minimal(),
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    AncestorIds = new[] { season.Id },
                    IsVirtualItem = false,
                    Recursive = true
                }).Count;

                seasons.Add(new DuplicateSeasonFinder.SeasonInfo(
                    series.Name ?? string.Empty,
                    seriesId,
                    season.IndexNumber,
                    season.Name ?? string.Empty,
                    season.Path,
                    season.Id.ToString("N", CultureInfo.InvariantCulture),
                    episodes));
            }
        }

        return seasons;
    }

    private IReadOnlyList<BaseItem> LoadOwned(params BaseItemKind[] kinds)
    {
        // Skip the load entirely for any kind the diagnosis cannot analyze.
        if (kinds.Any(k => !TitleIdentityDiagnosis.IsDiagnosable(k)))
        {
            return [];
        }

        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            DtoOptions = LibraryQueryOptions.WithProviderIds(),
            IncludeItemTypes = kinds,
            Recursive = true,

            // Owned means a real file: exclude virtual placeholders (e.g. minted items, missing entries) so
            // the diagnosis diffs against what is actually in the library.
            IsVirtualItem = false
        });
    }
}
