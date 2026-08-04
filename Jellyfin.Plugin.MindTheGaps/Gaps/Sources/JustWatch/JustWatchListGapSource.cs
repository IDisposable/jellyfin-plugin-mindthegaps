using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Gaps;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.JustWatch;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.JustWatch;

/// <summary>
/// Discovery source over the signed-in JustWatch account's own lists. It surfaces the titles on the watchlist
/// (and, when asked, the likes) that the library does not own as <see cref="GapPattern.Recommendation"/> gaps,
/// keyed by the TMDB/IMDb ids JustWatch records. Opt-in: needs a Discover toggle and the account's bearer
/// token, which JustWatch requires for any account data.
/// </summary>
/// <remarks>
/// There is no explore chip for this source: the lists are the account's fixed two, not something picked by id.
/// </remarks>
internal sealed class JustWatchListGapSource : IGapSource, IDiscoverSource
{
    // A watchlist is a want-list rather than a feed, so it is capped well above the 200 a community list gets.
    private const int MaxGapsPerList = 1000;

    private readonly JustWatchClient _justWatch;
    private readonly ILogger<JustWatchListGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JustWatchListGapSource"/> class.
    /// </summary>
    /// <param name="justWatch">The JustWatch client.</param>
    /// <param name="logger">The logger.</param>
    public JustWatchListGapSource(JustWatchClient justWatch, ILogger<JustWatchListGapSource> logger)
    {
        _justWatch = justWatch;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "JustWatch watchlist";

    /// <inheritdoc />
    public string DiscoverKind => SourceItemTypes.JustWatchList;

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie, BaseItemKind.Series };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanJustWatchLists && !string.IsNullOrWhiteSpace(config.JustWatchToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var listTypes = new List<string> { JustWatchListType.Watchlist };
        if (context.Config.ScanJustWatchLikes)
        {
            listTypes.Add(JustWatchListType.Likelist);
        }

        var country = string.IsNullOrWhiteSpace(context.Config.MetadataCountryCode) ? "US" : context.Config.MetadataCountryCode;
        var language = string.IsNullOrWhiteSpace(context.Config.MetadataLanguage) ? "en" : context.Config.MetadataLanguage;
        var done = 0;

        foreach (var listType in listTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ServiceCircuit.IsOpen(ServiceNames.JustWatch))
            {
                _logger.LogWarning("JustWatch: service unavailable this run; skipping the remaining lists");
                break;
            }

            IReadOnlyList<JustWatchTitle>? titles;
            try
            {
                titles = await _justWatch.GetListAsync(listType, country, language, MaxGapsPerList, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "JustWatch: failed to read {ListType}", listType);
                context.ReportProgress((double)++done / listTypes.Count);
                continue;
            }

            if (titles is null)
            {
                _logger.LogWarning(
                    "JustWatch: {ListType} could not be read; the token may be missing or expired",
                    listType);
                context.ReportProgress((double)++done / listTypes.Count);
                continue;
            }

            var name = JustWatchListType.DisplayName(listType);
            _logger.LogInformation("JustWatch: '{Name}' has {Count} titles", name, titles.Count);

            foreach (var gap in JustWatchListMapper.Build(listType, name, titles, context.Ownership, MaxGapsPerList))
            {
                yield return gap;
            }

            context.ReportProgress((double)++done / listTypes.Count);
        }
    }
}
