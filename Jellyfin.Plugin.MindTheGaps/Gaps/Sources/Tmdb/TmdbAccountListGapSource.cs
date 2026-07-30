using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Tmdb;

/// <summary>
/// Discovery source over the connected TheMovieDb account's own watchlist, and optionally its favorites.
/// Opt-in: needs the user's own TMDB api key and a session minted by the connect flow on the settings page.
/// </summary>
/// <remarks>
/// The api key requirement is not a preference. A TMDB session belongs to the application that created it,
/// and the key the catalog reader falls back to is Jellyfin's own, shared by every install, so a session must
/// never be minted through it. <see cref="TmdbAccountClient"/> enforces that; this source only runs when both
/// halves are present.
/// </remarks>
internal sealed class TmdbAccountListGapSource : IGapSource
{
    // A want-list is deliberate, so it is capped far above the 200 a community list gets.
    private const int MaxGapsPerList = 1000;

    private readonly TmdbAccountClient _account;
    private readonly TmdbClient _tmdb;
    private readonly ILogger<TmdbAccountListGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbAccountListGapSource"/> class.
    /// </summary>
    /// <param name="account">The TMDB account client.</param>
    /// <param name="tmdb">The TMDB catalog client, for its poster URL builder.</param>
    /// <param name="logger">The logger.</param>
    public TmdbAccountListGapSource(TmdbAccountClient account, TmdbClient tmdb, ILogger<TmdbAccountListGapSource> logger)
    {
        _account = account;
        _tmdb = tmdb;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "TMDB watchlist";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie, BaseItemKind.Series };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanTmdbWatchlist
            && !string.IsNullOrWhiteSpace(config.TmdbApiKey)
            && !string.IsNullOrWhiteSpace(config.TmdbSessionId);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var kinds = new List<TmdbAccountListKind> { TmdbAccountListKind.Watchlist };
        if (context.Config.ScanTmdbFavorites)
        {
            kinds.Add(TmdbAccountListKind.Favorites);
        }

        var done = 0;
        foreach (var kind in kinds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var movies = kind == TmdbAccountListKind.Favorites
                ? await _account.GetFavoriteMoviesAsync(MaxGapsPerList, cancellationToken).ConfigureAwait(false)
                : await _account.GetMovieWatchlistAsync(MaxGapsPerList, cancellationToken).ConfigureAwait(false);
            var series = kind == TmdbAccountListKind.Favorites
                ? await _account.GetFavoriteSeriesAsync(MaxGapsPerList, cancellationToken).ConfigureAwait(false)
                : await _account.GetSeriesWatchlistAsync(MaxGapsPerList, cancellationToken).ConfigureAwait(false);

            if (movies is null && series is null)
            {
                _logger.LogWarning(
                    "TMDB account: could not read the {List}; the session may have been revoked on themoviedb.org",
                    TmdbAccountListMapper.Label(kind));
                context.ReportProgress((double)++done / kinds.Count);
                continue;
            }

            _logger.LogInformation(
                "TMDB account: {List} holds {Movies} movies and {Series} series",
                TmdbAccountListMapper.Label(kind),
                movies?.Count ?? 0,
                series?.Count ?? 0);

            foreach (var gap in TmdbAccountListMapper.BuildMovies(movies ?? [], kind, context.Ownership, _tmdb.GetPosterUrl, MaxGapsPerList))
            {
                yield return gap;
            }

            foreach (var gap in TmdbAccountListMapper.BuildSeries(series ?? [], kind, context.Ownership, _tmdb.GetPosterUrl, MaxGapsPerList))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / kinds.Count);
        }
    }
}
