using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Services;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.TvMaze;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Supplies TVmaze's canonical episode list for a series, for the series-content merge. Keyless, opt-in,
/// rate-limited. Not a Jellyfin metadata fetcher, so it ranks below the named providers in the merge.
/// </summary>
internal sealed class TvMazeEpisodeProvider : ISeriesEpisodeProvider
{
    private readonly TvMazeClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvMazeEpisodeProvider"/> class.
    /// </summary>
    /// <param name="client">The TVmaze client.</param>
    public TvMazeEpisodeProvider(TvMazeClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    // The TVmaze plugin, when installed, registers a "TVmaze" metadata fetcher, so it ranks by its position
    // in the library's fetcher order like the others.
    public KnownProvider? Provider => KnownProviders.TvMaze;

    /// <inheritdoc />
    public string ServiceName => ServiceNames.TvMaze;

    /// <inheritdoc />
    public bool CanResolve(BaseItem series, PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(config);
        return config.ScanSeries
            && (series.ProviderIdOrNull(ProviderIds.TVmaze) is not null || series.ProviderIdOrNull(ProviderIds.Tvdb) is not null || series.ProviderIdOrNull(ProviderIds.Imdb) is not null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CanonicalEpisode>?> GetCanonicalEpisodesAsync(
        BaseItem series,
        GapScanContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(context);

        var showId = await ResolveShowIdAsync(series, cancellationToken).ConfigureAwait(false);
        if (showId is null)
        {
            return null;
        }

        var episodes = await _client.GetEpisodesAsync(showId.Value, cancellationToken).ConfigureAwait(false);
        return episodes is null ? null : TvMazeMapper.ToCanonical(episodes);
    }

    private async Task<int?> ResolveShowIdAsync(BaseItem series, CancellationToken cancellationToken)
    {
        if (series.ProviderIdOrNull(ProviderIds.TVmaze) is { } direct
            && int.TryParse(direct, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tvMazeId))
        {
            return tvMazeId;
        }

        if (series.ProviderIdOrNull(ProviderIds.Tvdb) is { } tvdb)
        {
            var resolved = await _client.ResolveShowIdAsync("thetvdb", tvdb, cancellationToken).ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (series.ProviderIdOrNull(ProviderIds.Imdb) is { } imdb)
        {
            return await _client.ResolveShowIdAsync("imdb", imdb, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }
}
