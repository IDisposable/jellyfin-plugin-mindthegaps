using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using TMDbLib.Objects.Search;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Widens SetCompletion beyond formal TMDB BoxSets to curated sets: the movies of a studio (TMDB
/// company) or tagged with a keyword. Tracks the studios/keywords the user configures (by TMDB id) and
/// surfaces the ones the library does not own, grouped by the set's name. Opt-in (off by default).
/// </summary>
public sealed class CuratedSetGapSource : IGapSource
{
    // 20 results per discover page; cap pages and emitted gaps so a broad studio does not flood the list.
    private const int MaxPagesPerSet = 10;
    private const int MaxGapsPerSet = 150;

    // Auto-seed bounds: a studio must credit at least this many owned items to be worth tracking, and at
    // most this many auto-seeded studios are taken (most-owned first).
    private const int MinOwnedForStudio = 3;
    private const int MaxAutoStudios = 20;

    private readonly TmdbClient _tmdb;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<CuratedSetGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CuratedSetGapSource"/> class.
    /// </summary>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="libraryManager">The library manager (for auto-seeding studios from owned items).</param>
    /// <param name="logger">The logger.</param>
    public CuratedSetGapSource(TmdbClient tmdb, ILibraryManager libraryManager, ILogger<CuratedSetGapSource> logger)
    {
        _tmdb = tmdb;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Curated sets";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanCuratedSets
            && (ParseIds(config.CuratedCompanyIds).Count > 0
                || ParseIds(config.CuratedKeywordIds).Count > 0
                || ParseIds(config.CuratedTmdbListIds).Count > 0
                || config.AutoSeedStudios);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var companySets = await BuildCompanySetsAsync(context.Config, cancellationToken).ConfigureAwait(false);
        var keywordIds = ParseIds(context.Config.CuratedKeywordIds);
        var listIds = ParseIds(context.Config.CuratedTmdbListIds);
        var total = Math.Max(1, companySets.Count + keywordIds.Count + listIds.Count);
        var done = 0;
        _logger.LogInformation("Curated sets: scanning {Companies} studios, {Keywords} keywords, and {Lists} TMDB lists", companySets.Count, keywordIds.Count, listIds.Count);

        foreach (var (companyId, label) in companySets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var gap in EmitCompanyAsync(context, companyId, label, cancellationToken).ConfigureAwait(false))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / total);
        }

        foreach (var keywordId in keywordIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var gap in EmitKeywordAsync(context, keywordId, cancellationToken).ConfigureAwait(false))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / total);
        }

        foreach (var listId in listIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var gap in EmitListAsync(context, listId, cancellationToken).ConfigureAwait(false))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / total);
        }
    }

    /// <summary>
    /// Streams the gaps for an explicit set of ids of one curated kind ("studio", "keyword", or
    /// "tmdblist"), diffed against the context's ownership index. The scan path runs the configured ids of
    /// every kind; an ad-hoc "explore a source" run calls this with one kind and the ids the user picked,
    /// so a single studio, keyword, or TMDB list can be surfaced without a full rescan. Auto-seeded studios
    /// are a scan-only concern and are not consulted here.
    /// </summary>
    /// <param name="context">The scan context.</param>
    /// <param name="kind">The curated kind: "studio", "keyword", or "tmdblist".</param>
    /// <param name="ids">The TMDB ids to fetch and diff for that kind.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>An async stream of gaps.</returns>
    public async IAsyncEnumerable<GapItem> FindGapsForSetsAsync(
        GapScanContext context,
        string kind,
        IReadOnlyList<int> ids,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ids);

        var total = Math.Max(1, ids.Count);
        var done = 0;

        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();

            IAsyncEnumerable<GapItem> stream;
            if (string.Equals(kind, "keyword", StringComparison.OrdinalIgnoreCase))
            {
                var label = await SafeKeywordName(id, ct).ConfigureAwait(false)
                    ?? string.Create(CultureInfo.InvariantCulture, $"Keyword {id}");
                stream = EmitKeywordAsync(context, id, label, ct);
            }
            else if (string.Equals(kind, "tmdblist", StringComparison.OrdinalIgnoreCase))
            {
                stream = EmitListAsync(context, id, ct);
            }
            else
            {
                var label = await SafeCompanyName(id, ct).ConfigureAwait(false)
                    ?? string.Create(CultureInfo.InvariantCulture, $"Studio {id}");
                stream = EmitCompanyAsync(context, id, label, ct);
            }

            await foreach (var gap in stream.ConfigureAwait(false))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / total);
        }
    }

    private async IAsyncEnumerable<GapItem> EmitCompanyAsync(
        GapScanContext context,
        int companyId,
        string label,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var language = context.Config.MetadataLanguage;
        var results = await CollectAsync(
            (page, ct) => _tmdb.DiscoverMoviesByCompanyAsync(companyId, page, language, ct),
            cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Curated sets: studio '{Label}' ({Id}) has {Count} movies on TMDB", label, companyId, results.Count);

        foreach (var gap in CuratedSetGapMapper.BuildMovies(
            results,
            string.Create(CultureInfo.InvariantCulture, $"company:{companyId}"),
            label,
            "Studio",
            context.Ownership,
            _tmdb.GetPosterUrl,
            MaxGapsPerSet))
        {
            yield return gap;
        }
    }

    private IAsyncEnumerable<GapItem> EmitKeywordAsync(
        GapScanContext context,
        int keywordId,
        CancellationToken cancellationToken)
        => EmitKeywordAsync(context, keywordId, label: null, cancellationToken);

    private async IAsyncEnumerable<GapItem> EmitKeywordAsync(
        GapScanContext context,
        int keywordId,
        string? label,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var language = context.Config.MetadataLanguage;
        label ??= await SafeKeywordName(keywordId, cancellationToken).ConfigureAwait(false)
            ?? string.Create(CultureInfo.InvariantCulture, $"Keyword {keywordId}");
        var results = await CollectAsync(
            (page, ct) => _tmdb.DiscoverMoviesByKeywordAsync(keywordId, page, language, ct),
            cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Curated sets: keyword '{Label}' ({Id}) has {Count} movies on TMDB", label, keywordId, results.Count);

        foreach (var gap in CuratedSetGapMapper.BuildMovies(
            results,
            string.Create(CultureInfo.InvariantCulture, $"keyword:{keywordId}"),
            label,
            "Keyword",
            context.Ownership,
            _tmdb.GetPosterUrl,
            MaxGapsPerSet))
        {
            yield return gap;
        }
    }

    private async IAsyncEnumerable<GapItem> EmitListAsync(
        GapScanContext context,
        int listId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var language = context.Config.MetadataLanguage;
        var list = await _tmdb.GetListMoviesAsync(listId, language, cancellationToken).ConfigureAwait(false);
        var label = string.IsNullOrEmpty(list.Name)
            ? string.Create(CultureInfo.InvariantCulture, $"List {listId}")
            : list.Name;
        _logger.LogInformation("Curated sets: TMDB list '{Label}' ({Id}) has {Count} movies", label, listId, list.Movies.Count);

        foreach (var gap in CuratedSetGapMapper.BuildMovies(
            list.Movies,
            string.Create(CultureInfo.InvariantCulture, $"list:{listId}"),
            label,
            "List",
            context.Ownership,
            _tmdb.GetPosterUrl,
            MaxGapsPerSet,
            GapPattern.Recommendation,
            string.Create(CultureInfo.InvariantCulture, $"tmdblist-{listId}")))
        {
            yield return gap;
        }
    }

    // Page through a discover query up to the page cap, accumulating the results.
    private static async Task<List<SearchMovie>> CollectAsync(
        Func<int, CancellationToken, Task<(IReadOnlyList<SearchMovie> Results, int TotalPages)>> fetch,
        CancellationToken cancellationToken)
    {
        var all = new List<SearchMovie>();
        var page = 1;
        var totalPages = 1;
        while (page <= totalPages && page <= MaxPagesPerSet)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (results, pages) = await fetch(page, cancellationToken).ConfigureAwait(false);
            if (results.Count == 0)
            {
                break;
            }

            all.AddRange(results);
            totalPages = pages;
            page++;
        }

        return all;
    }

    // Build the studios to scan from: the configured ids and, when auto-seed is on, the most-owned studios.
    // De-duped by company id, first label wins.
    private async Task<List<(int Id, string Label)>> BuildCompanySetsAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        var sets = new List<(int Id, string Label)>();
        var seen = new HashSet<int>();

        foreach (var id in ParseIds(config.CuratedCompanyIds))
        {
            if (!seen.Add(id))
            {
                continue;
            }

            var name = await SafeCompanyName(id, cancellationToken).ConfigureAwait(false)
                ?? string.Create(CultureInfo.InvariantCulture, $"Studio {id}");
            sets.Add((id, name));
        }

        if (config.AutoSeedStudios)
        {
            foreach (var studio in TopOwnedStudios())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resolved = await SafeSearchCompany(studio, cancellationToken).ConfigureAwait(false);
                if (resolved is { } match && seen.Add(match.Id))
                {
                    sets.Add((match.Id, match.Name));
                }
            }
        }

        return sets;
    }

    // The studios crediting the most owned movies/series (above a floor), most-owned first.
    private IReadOnlyList<string> TopOwnedStudios()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var owned = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true
        });

        foreach (var item in owned)
        {
            foreach (var studio in item.Studios)
            {
                if (!string.IsNullOrWhiteSpace(studio))
                {
                    counts.TryGetValue(studio, out var current);
                    counts[studio] = current + 1;
                }
            }
        }

        return counts
            .Where(kv => kv.Value >= MinOwnedForStudio)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaxAutoStudios)
            .Select(kv => kv.Key)
            .ToList();
    }

    private async Task<(int Id, string Name)?> SafeSearchCompany(string name, CancellationToken cancellationToken)
    {
        try
        {
            return await _tmdb.SearchCompanyAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Curated sets: studio search failed for '{Name}'", name);
            return null;
        }
    }

    private async Task<string?> SafeCompanyName(int companyId, CancellationToken cancellationToken)
    {
        try
        {
            return await _tmdb.GetCompanyNameAsync(companyId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Curated sets: failed to fetch company name for {Id}", companyId);
            return null;
        }
    }

    private async Task<string?> SafeKeywordName(int keywordId, CancellationToken cancellationToken)
    {
        try
        {
            return await _tmdb.GetKeywordNameAsync(keywordId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Curated sets: failed to fetch keyword name for {Id}", keywordId);
            return null;
        }
    }

    // Parse a comma-separated list of TMDB ids, ignoring blanks and non-numbers.
    private static IReadOnlyList<int> ParseIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<int>();
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (int?)null)
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
    }
}
