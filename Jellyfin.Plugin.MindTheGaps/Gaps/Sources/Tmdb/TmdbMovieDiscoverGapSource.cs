using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Microsoft.Extensions.Logging;
using TMDbLib.Objects.Search;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Discovery source over TMDB's own official movie feeds: Top Rated, Popular, Upcoming, and Now Playing.
/// Unlike a TMDB List (a user-entered id) these are fixed, id-less endpoints, so each is its own toggle
/// rather than an id a user configures; each feed groups as its own dismissible entry under Discover.
/// </summary>
internal sealed class TmdbMovieDiscoverGapSource : IGapSource, IDiscoverSource
{
    // TMDB paginates these at 20 results/page, arbitrarily deep (hundreds of pages) for a live chart, not
    // a fixed "top 20" list, so both caps sit at the same level CuratedSetGapSource uses for its own
    // TMDB-curated sets rather than a number tuned to a single page.
    private const int MaxPagesPerFeed = 10;
    private const int MaxGapsPerFeed = 150;

    private static readonly (string Kind, string Label)[] Feeds =
    {
        ("top_rated", "Top Rated (TMDB)"),
        ("popular", "Popular (TMDB)"),
        ("upcoming", "Upcoming (TMDB)"),
        ("now_playing", "Now Playing (TMDB)")
    };

    private readonly TmdbClient _tmdb;
    private readonly ILogger<TmdbMovieDiscoverGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbMovieDiscoverGapSource"/> class.
    /// </summary>
    /// <param name="tmdb">The TMDB client.</param>
    /// <param name="logger">The logger.</param>
    public TmdbMovieDiscoverGapSource(TmdbClient tmdb, ILogger<TmdbMovieDiscoverGapSource> logger)
    {
        _tmdb = tmdb;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "TMDB discover feeds";

    /// <inheritdoc />
    public string DiscoverKind => SourceItemTypes.TmdbMovieDiscover;

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanTmdbTopRated || config.ScanTmdbPopular || config.ScanTmdbUpcoming || config.ScanTmdbNowPlaying;

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var config = context.Config;
        var language = config.MetadataLanguage;
        var region = config.MetadataCountryCode;

        var enabled = new List<(string Kind, string Label)>();
        foreach (var feed in Feeds)
        {
            if (IsFeedEnabled(config, feed.Kind))
            {
                enabled.Add(feed);
            }
        }

        var done = 0;
        foreach (var (kind, label) in enabled)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var results = await CollectAsync(kind, language, region, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("TMDB discover: {Kind} has {Count} movies", kind, results.Count);

            foreach (var gap in CuratedSetGapMapper.BuildMovies(
                results,
                kind,
                label,
                SourceItemTypes.TmdbMovieDiscover,
                context.Ownership,
                _tmdb.GetPosterUrl,
                MaxGapsPerFeed,
                GapPattern.Recommendation,
                GapSourceKeys.TmdbMovieDiscover.Owner(kind),
                GapSourceKeys.TmdbMovieDiscover.GapPrefix))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / enabled.Count);
        }
    }

    private static bool IsFeedEnabled(PluginConfiguration config, string kind) => kind switch
    {
        "top_rated" => config.ScanTmdbTopRated,
        "popular" => config.ScanTmdbPopular,
        "upcoming" => config.ScanTmdbUpcoming,
        "now_playing" => config.ScanTmdbNowPlaying,
        _ => false
    };

    // Pages one feed up to the page cap, accumulating the results, mirroring CuratedSetGapSource's own
    // CollectAsync (same shape, different fetch delegate per feed kind).
    private async Task<List<SearchMovie>> CollectAsync(string kind, string? language, string? region, CancellationToken cancellationToken)
    {
        var all = new List<SearchMovie>();
        var page = 1;
        var totalPages = 1;
        while (page <= totalPages && page <= MaxPagesPerFeed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (results, pages) = await FetchPageAsync(kind, page, language, region, cancellationToken).ConfigureAwait(false);
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

    private Task<(IReadOnlyList<SearchMovie> Results, int TotalPages)> FetchPageAsync(
        string kind,
        int page,
        string? language,
        string? region,
        CancellationToken cancellationToken) => kind switch
        {
            "top_rated" => _tmdb.GetTopRatedMoviesAsync(page, language, region, cancellationToken),
            "popular" => _tmdb.GetPopularMoviesAsync(page, language, region, cancellationToken),
            "upcoming" => _tmdb.GetUpcomingMoviesAsync(page, language, region, cancellationToken),
            "now_playing" => _tmdb.GetNowPlayingMoviesAsync(page, language, region, cancellationToken),
            _ => Task.FromResult<(IReadOnlyList<SearchMovie> Results, int TotalPages)>(([], 0))
        };
}
