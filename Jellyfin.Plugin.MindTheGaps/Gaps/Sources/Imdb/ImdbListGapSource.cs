using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Imdb;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Imdb;

/// <summary>
/// Discovery source over the IMDb lists (not watchlists) in the configured ids.
/// </summary>
internal sealed class ImdbListGapSource : ImdbTitleListGapSourceBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImdbListGapSource"/> class.
    /// </summary>
    /// <param name="imdb">The IMDb client.</param>
    /// <param name="logger">The logger.</param>
    public ImdbListGapSource(ImdbClient imdb, ILogger<ImdbListGapSource> logger)
        : base(imdb, logger)
    {
    }

    /// <inheritdoc />
    public override string Name => "IMDb lists";

    /// <inheritdoc />
    public override string DiscoverKind => SourceItemTypes.ImdbList;

    /// <inheritdoc />
    protected override bool ReadsWatchlists => false;
}
