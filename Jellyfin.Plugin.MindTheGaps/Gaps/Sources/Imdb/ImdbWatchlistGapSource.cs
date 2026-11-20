using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Imdb;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Imdb;

/// <summary>
/// Discovery source over the IMDb watchlists in the configured ids, by user id or by the watchlist's own
/// list id.
/// </summary>
internal sealed class ImdbWatchlistGapSource : ImdbTitleListGapSourceBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImdbWatchlistGapSource"/> class.
    /// </summary>
    /// <param name="imdb">The IMDb client.</param>
    /// <param name="logger">The logger.</param>
    public ImdbWatchlistGapSource(ImdbClient imdb, ILogger<ImdbWatchlistGapSource> logger)
        : base(imdb, logger)
    {
    }

    /// <inheritdoc />
    public override string Name => "IMDb watchlists";

    /// <inheritdoc />
    public override string DiscoverKind => SourceItemTypes.ImdbWatchlist;

    /// <inheritdoc />
    protected override bool ReadsWatchlists => true;
}
