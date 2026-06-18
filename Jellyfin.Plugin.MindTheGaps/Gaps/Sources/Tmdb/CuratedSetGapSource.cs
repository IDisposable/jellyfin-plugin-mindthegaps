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
                || ParseNames(config.CuratedCompanyNames).Count > 0
                || config.AutoSeedStudios);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var language = context.Config.MetadataLanguage;
        var companySets = await BuildCompanySetsAsync(context.Config, cancellationToken).ConfigureAwait(false);
        var keywordIds = ParseIds(context.Config.CuratedKeywordIds);
        var total = Math.Max(1, companySets.Count + keywordIds.Count);
        var done = 0;
        _logger.LogInformation("Curated sets: scanning {Companies} studios and {Keywords} keywords", companySets.Count, keywordIds.Count);

        foreach (var (companyId, label) in companySets)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            context.ReportProgress((double)++done / total);
        }

        foreach (var keywordId in keywordIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = await CollectAsync(
                (page, ct) => _tmdb.DiscoverMoviesByKeywordAsync(keywordId, page, language, ct),
                cancellationToken).ConfigureAwait(false);

            foreach (var gap in CuratedSetGapMapper.BuildMovies(
                results,
                string.Create(CultureInfo.InvariantCulture, $"keyword:{keywordId}"),
                string.Create(CultureInfo.InvariantCulture, $"Keyword {keywordId}"),
                "Keyword",
                context.Ownership,
                _tmdb.GetPosterUrl,
                MaxGapsPerSet))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / total);
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

    // Build the studios to scan from: explicit ids, explicit names (resolved), and the most-owned studios
    // (when auto-seed is on). De-duped by company id, first label wins.
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

        foreach (var name in ParseNames(config.CuratedCompanyNames))
        {
            var resolved = await SafeSearchCompany(name, cancellationToken).ConfigureAwait(false);
            if (resolved is { } match && seen.Add(match.Id))
            {
                sets.Add((match.Id, match.Name));
            }
            else if (resolved is null)
            {
                _logger.LogInformation("Curated sets: no TMDB studio matched '{Name}'", name);
            }
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

    // Parse a comma-separated list of studio names, trimmed and de-duplicated.
    private static IReadOnlyList<string> ParseNames(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
