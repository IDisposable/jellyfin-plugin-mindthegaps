using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Tvdb;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Discovery source over the TheTVDB account's favorite series: the shows marked as favorites that the
/// library does not hold. Opt-in, and it needs the TheTVDB API key the episode cross-check already uses plus
/// a subscriber PIN, because favorites are account data and a key-only token cannot read them.
/// </summary>
/// <remarks>
/// A favorite is usually something already owned, so this yields far less than the other want-lists. It is
/// worth having for the few that are not: a show followed on TheTVDB but never acquired.
/// </remarks>
internal sealed class TvdbFavoritesGapSource : IGapSource, IDiscoverSource
{
    // Favorites are a hand-curated set, rarely more than a few dozen.
    private const int MaxGaps = 500;

    private readonly TvdbClient _tvdb;
    private readonly ILogger<TvdbFavoritesGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvdbFavoritesGapSource"/> class.
    /// </summary>
    /// <param name="tvdb">TheTVDB client.</param>
    /// <param name="logger">The logger.</param>
    public TvdbFavoritesGapSource(TvdbClient tvdb, ILogger<TvdbFavoritesGapSource> logger)
    {
        _tvdb = tvdb;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "TheTVDB favorites";

    /// <inheritdoc />
    public string DiscoverKind => SourceItemTypes.TvdbFavorites;

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Series };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanTvdbFavorites
            && !string.IsNullOrWhiteSpace(config.TvdbApiKey)
            && !string.IsNullOrWhiteSpace(config.TvdbPin);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (ServiceCircuit.IsOpen(ServiceNames.Tvdb))
        {
            _logger.LogWarning("TheTVDB favorites: service unavailable this run");
            yield break;
        }

        var apiKey = context.Config.TvdbApiKey;
        IReadOnlyList<long>? favorites;
        try
        {
            favorites = await _tvdb.GetFavoriteSeriesIdsAsync(apiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "TheTVDB favorites: failed to read the favorites");
            yield break;
        }

        if (favorites is null)
        {
            _logger.LogWarning(
                "TheTVDB favorites: could not be read; check the API key and the subscriber PIN, which is what scopes the token to the account");
            yield break;
        }

        _logger.LogInformation("TheTVDB favorites: {Count} favorite series", favorites.Count);

        var emitted = 0;
        for (var index = 0; index < favorites.Count && emitted < MaxGaps; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress((double)index / Math.Max(1, favorites.Count));

            var seriesId = favorites[index];
            var providerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderIds.Tvdb] = seriesId.ToString(CultureInfo.InvariantCulture)
            };

            // Check ownership before the lookup: a favorite is usually something already held, and the
            // record fetch is a network call per series.
            if (context.Ownership.OwnsAny(BaseItemKind.Series, providerIds))
            {
                continue;
            }

            TvdbSeriesRecord? series;
            try
            {
                series = await _tvdb.GetSeriesAsync(apiKey, seriesId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "TheTVDB favorites: failed to read series {SeriesId}", seriesId);
                continue;
            }

            if (series?.Name is not { Length: > 0 } name)
            {
                continue;
            }

            emitted++;
            yield return GapItemFactory.Create(
                id: string.Create(CultureInfo.InvariantCulture, $"{GapSourceKeys.TvdbFavorites.GapPrefix}{seriesId}"),
                pattern: GapPattern.Recommendation,
                domain: MediaDomain.Shows,
                targetKind: BaseItemKind.Series,
                name: name,
                providerIds: providerIds,
                sourceItemId: GapSourceKeys.TvdbFavorites.Owner(),
                sourceItemName: "TheTVDB favorites",
                sourceItemType: SourceItemTypes.TvdbFavorites,
                releaseDate: ParseDate(series.FirstAired),
                imageUrl: series.Image);
        }

        context.ReportProgress(1);
    }

    private static DateTime? ParseDate(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date
            : null;
}
