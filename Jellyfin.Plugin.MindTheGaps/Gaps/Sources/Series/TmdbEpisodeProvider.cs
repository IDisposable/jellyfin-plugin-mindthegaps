using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Services;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Supplies TheMovieDb's canonical episode list for a series, for the series-content merge. Opt-in and
/// keyless (the plugin's shared TheMovieDb key), so a library that follows TheMovieDb is diffed against its
/// own numbering.
/// </summary>
public sealed class TmdbEpisodeProvider : ISeriesEpisodeProvider
{
    private readonly TmdbClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbEpisodeProvider"/> class.
    /// </summary>
    /// <param name="client">The TheMovieDb client.</param>
    public TmdbEpisodeProvider(TmdbClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public KnownProvider? Provider => KnownProviders.Tmdb;

    /// <inheritdoc />
    public string ServiceName => ServiceNames.Tmdb;

    /// <inheritdoc />
    public bool CanResolve(BaseItem series, PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(config);
        return config.ScanSeries && series.ProviderIdOrNull(ProviderIds.Tmdb) is not null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CanonicalEpisode>?> GetCanonicalEpisodesAsync(
        BaseItem series,
        GapScanContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(context);

        if (series.ProviderIdOrNull(ProviderIds.Tmdb) is not { } raw
            || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
        {
            return null;
        }

        var episodes = await _client.GetSeriesEpisodesAsync(tmdbId, context.Config.MetadataLanguage, cancellationToken).ConfigureAwait(false);
        return episodes.Count == 0 ? null : TmdbSeriesMapper.ToCanonical(episodes);
    }
}
