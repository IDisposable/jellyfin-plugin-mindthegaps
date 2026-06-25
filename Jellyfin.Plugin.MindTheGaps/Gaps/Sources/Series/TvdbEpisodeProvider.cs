using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Services;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Tvdb;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Supplies TheTVDB's canonical episode list for a series, for the series-content merge. Opt-in; requires
/// the user's own TheTVDB v4 API key.
/// </summary>
internal sealed class TvdbEpisodeProvider : ISeriesEpisodeProvider
{
    private readonly TvdbClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvdbEpisodeProvider"/> class.
    /// </summary>
    /// <param name="client">The TheTVDB client.</param>
    public TvdbEpisodeProvider(TvdbClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public KnownProvider? Provider => KnownProviders.Tvdb;

    /// <inheritdoc />
    public string ServiceName => ServiceNames.Tvdb;

    /// <inheritdoc />
    public bool CanResolve(BaseItem series, PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(config);
        return config.ScanSeries
            && !string.IsNullOrWhiteSpace(config.TvdbApiKey)
            && (series.ProviderIdOrNull(ProviderIds.Tvdb) is not null || series.ProviderIdOrNull(ProviderIds.Imdb) is not null || series.ProviderIdOrNull(ProviderIds.Tmdb) is not null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CanonicalEpisode>?> GetCanonicalEpisodesAsync(
        BaseItem series,
        GapScanContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(context);

        var apiKey = context.Config.TvdbApiKey;

        var seriesId = await ResolveSeriesIdAsync(series, apiKey, cancellationToken).ConfigureAwait(false);
        if (seriesId is null)
        {
            return null;
        }

        var episodes = await _client.GetEpisodesAsync(apiKey, seriesId.Value, cancellationToken).ConfigureAwait(false);
        return episodes is null ? null : TvdbMapper.ToCanonical(episodes);
    }

    private async Task<long?> ResolveSeriesIdAsync(BaseItem series, string apiKey, CancellationToken cancellationToken)
    {
        if (series.ProviderIdOrNull(ProviderIds.Tvdb) is { } direct
            && long.TryParse(direct, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tvdbId))
        {
            return tvdbId;
        }

        if (series.ProviderIdOrNull(ProviderIds.Imdb) is { } imdb)
        {
            var resolved = await _client.ResolveSeriesIdAsync(apiKey, imdb, cancellationToken).ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (series.ProviderIdOrNull(ProviderIds.Tmdb) is { } tmdb)
        {
            return await _client.ResolveSeriesIdAsync(apiKey, tmdb, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }
}
