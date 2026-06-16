using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Services.Tvdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Cross-checks owned series against TheTVDB's canonical episode list. Opt-in; requires the user's
/// own TheTVDB v4 API key.
/// </summary>
public sealed class TvdbContentGapSource : SeriesContentGapSourceBase
{
    private readonly TvdbClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvdbContentGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="client">The TheTVDB client.</param>
    /// <param name="logger">The logger.</param>
    public TvdbContentGapSource(ILibraryManager libraryManager, TvdbClient client, ILogger<TvdbContentGapSource> logger)
        : base(libraryManager, logger)
    {
        _client = client;
    }

    /// <inheritdoc />
    public override string Name => "Series content (TheTVDB)";

    /// <inheritdoc />
    protected override int MaxSeries => 300;

    /// <inheritdoc />
    public override bool IsEnabled(PluginConfiguration config)
        => config.ScanSeries && config.TvdbEnabled && !string.IsNullOrWhiteSpace(config.TvdbApiKey);

    /// <inheritdoc />
    protected override bool HasLookupId(BaseItem series)
        => Id(series, MetadataProvider.Tvdb.ToString()) is not null
            || Id(series, MetadataProvider.Imdb.ToString()) is not null
            || Id(series, MetadataProvider.Tmdb.ToString()) is not null;

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<CanonicalEpisode>?> GetCanonicalEpisodesAsync(
        BaseItem series,
        GapScanContext context,
        CancellationToken cancellationToken)
    {
        var apiKey = context.Config.TvdbApiKey;

        var seriesId = await ResolveSeriesIdAsync(series, apiKey, cancellationToken).ConfigureAwait(false);
        if (seriesId is null)
        {
            return null;
        }

        var episodes = await _client.GetEpisodesAsync(apiKey, seriesId.Value, cancellationToken).ConfigureAwait(false);
        return episodes is null ? null : TvdbMapper.ToCanonical(episodes);
    }

    private static string? Id(BaseItem item, string provider)
        => item.TryGetProviderId(provider, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    private async Task<long?> ResolveSeriesIdAsync(BaseItem series, string apiKey, CancellationToken cancellationToken)
    {
        if (Id(series, MetadataProvider.Tvdb.ToString()) is { } direct
            && long.TryParse(direct, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tvdbId))
        {
            return tvdbId;
        }

        if (Id(series, MetadataProvider.Imdb.ToString()) is { } imdb)
        {
            var resolved = await _client.ResolveSeriesIdAsync(apiKey, imdb, cancellationToken).ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (Id(series, MetadataProvider.Tmdb.ToString()) is { } tmdb)
        {
            return await _client.ResolveSeriesIdAsync(apiKey, tmdb, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }
}
