using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Services.TvMaze;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Cross-checks owned series against TVmaze's canonical episode list. Keyless, opt-in. Catches
/// episodes a series' configured metadata provider doesn't know about.
/// </summary>
public sealed class TvMazeContentGapSource : SeriesContentGapSourceBase
{
    private const string TvMazeProvider = "TVmaze";

    private readonly TvMazeClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvMazeContentGapSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="client">The TVmaze client.</param>
    /// <param name="cursors">The scan-rotation cursor store.</param>
    /// <param name="logger">The logger.</param>
    public TvMazeContentGapSource(ILibraryManager libraryManager, TvMazeClient client, ScanCursorStore cursors, ILogger<TvMazeContentGapSource> logger)
        : base(libraryManager, cursors, logger)
    {
        _client = client;
    }

    /// <inheritdoc />
    public override string Name => "Series content (TVmaze)";

    /// <inheritdoc />
    // Keyless but rate-limited (~20 calls / 10s), so bound the per-run series count.
    protected override int MaxSeries => 300;

    /// <inheritdoc />
    protected override string ServiceName => "TVmaze";

    /// <inheritdoc />
    public override bool IsEnabled(PluginConfiguration config) => config.ScanSeries && config.TvMazeEnabled;

    /// <inheritdoc />
    protected override bool HasLookupId(BaseItem series)
        => Id(series, TvMazeProvider) is not null
            || Id(series, MetadataProvider.Tvdb.ToString()) is not null
            || Id(series, MetadataProvider.Imdb.ToString()) is not null;

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<CanonicalEpisode>?> GetCanonicalEpisodesAsync(
        BaseItem series,
        GapScanContext context,
        CancellationToken cancellationToken)
    {
        var showId = await ResolveShowIdAsync(series, cancellationToken).ConfigureAwait(false);
        if (showId is null)
        {
            return null;
        }

        var episodes = await _client.GetEpisodesAsync(showId.Value, cancellationToken).ConfigureAwait(false);
        return episodes is null ? null : TvMazeMapper.ToCanonical(episodes);
    }

    private static string? Id(BaseItem item, string provider)
        => item.TryGetProviderId(provider, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    private async Task<int?> ResolveShowIdAsync(BaseItem series, CancellationToken cancellationToken)
    {
        if (Id(series, TvMazeProvider) is { } direct
            && int.TryParse(direct, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tvMazeId))
        {
            return tvMazeId;
        }

        if (Id(series, MetadataProvider.Tvdb.ToString()) is { } tvdb)
        {
            var resolved = await _client.ResolveShowIdAsync("thetvdb", tvdb, cancellationToken).ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (Id(series, MetadataProvider.Imdb.ToString()) is { } imdb)
        {
            return await _client.ResolveShowIdAsync("imdb", imdb, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }
}
