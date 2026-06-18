using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
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

    private readonly TmdbClient _tmdb;
    private readonly ILogger<CuratedSetGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CuratedSetGapSource"/> class.
    /// </summary>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="logger">The logger.</param>
    public CuratedSetGapSource(TmdbClient tmdb, ILogger<CuratedSetGapSource> logger)
    {
        _tmdb = tmdb;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Curated sets";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanCuratedSets
            && (ParseIds(config.CuratedCompanyIds).Count > 0 || ParseIds(config.CuratedKeywordIds).Count > 0);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var language = context.Config.MetadataLanguage;
        var companyIds = ParseIds(context.Config.CuratedCompanyIds);
        var keywordIds = ParseIds(context.Config.CuratedKeywordIds);
        var total = Math.Max(1, companyIds.Count + keywordIds.Count);
        var done = 0;

        foreach (var companyId in companyIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var label = await SafeCompanyName(companyId, cancellationToken).ConfigureAwait(false)
                ?? string.Create(CultureInfo.InvariantCulture, $"Studio {companyId}");
            var results = await CollectAsync(
                (page, ct) => _tmdb.DiscoverMoviesByCompanyAsync(companyId, page, language, ct),
                cancellationToken).ConfigureAwait(false);

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
    private static async System.Threading.Tasks.Task<List<SearchMovie>> CollectAsync(
        Func<int, CancellationToken, System.Threading.Tasks.Task<(IReadOnlyList<SearchMovie> Results, int TotalPages)>> fetch,
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

    private async System.Threading.Tasks.Task<string?> SafeCompanyName(int companyId, CancellationToken cancellationToken)
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
