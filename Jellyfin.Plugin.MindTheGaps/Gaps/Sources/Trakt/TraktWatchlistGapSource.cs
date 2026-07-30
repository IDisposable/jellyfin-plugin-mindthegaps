using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Http;
using Jellyfin.Plugin.MindTheGaps.Services.Trakt;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Trakt;

/// <summary>
/// Discovery source over a Trakt user's watchlist: the movies and shows they have marked and the library does
/// not hold. Opt-in, needing the username and the Trakt client id the list and filmography sources already
/// use; Trakt serves a public profile's watchlist without OAuth.
/// </summary>
internal sealed class TraktWatchlistGapSource : IGapSource
{
    // A watchlist is a deliberate list, so it is capped far above the 200 a community list gets. A truncation
    // is logged rather than silent, because a very large watchlist can genuinely exceed this.
    private const int MaxGaps = 1000;

    private readonly TraktClient _trakt;
    private readonly ILogger<TraktWatchlistGapSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraktWatchlistGapSource"/> class.
    /// </summary>
    /// <param name="trakt">The Trakt client.</param>
    /// <param name="logger">The logger.</param>
    public TraktWatchlistGapSource(TraktClient trakt, ILogger<TraktWatchlistGapSource> logger)
    {
        _trakt = trakt;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Trakt watchlist";

    /// <inheritdoc />
    public IReadOnlyCollection<BaseItemKind> OwnedKinds { get; } = new[] { BaseItemKind.Movie, BaseItemKind.Series };

    /// <inheritdoc />
    public bool IsEnabled(PluginConfiguration config)
        => config.ScanTraktWatchlist
            && !string.IsNullOrWhiteSpace(config.TraktClientId)
            && !string.IsNullOrWhiteSpace(config.TraktUsername);

    /// <inheritdoc />
    public async IAsyncEnumerable<GapItem> FindGapsAsync(
        GapScanContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (ServiceCircuit.IsOpen(ServiceNames.Trakt))
        {
            _logger.LogWarning("Trakt watchlist: service unavailable this run");
            yield break;
        }

        var username = context.Config.TraktUsername.Trim();
        IReadOnlyList<TraktListItem>? items;
        try
        {
            items = await _trakt.GetWatchlistAsync(username, MaxGaps, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Trakt watchlist: failed to read the watchlist of {User}", username);
            yield break;
        }

        if (items is null)
        {
            _logger.LogWarning("Trakt watchlist: could not read the watchlist of {User}", username);
            yield break;
        }

        // Trakt answers 200 with an empty array for an empty watchlist, a private profile, and a username
        // that does not exist alike, so this cannot say which it was; the count is all there is to report.
        if (items.Count == 0)
        {
            _logger.LogInformation(
                "Trakt watchlist: nothing on the watchlist of {User}; Trakt answers the same way for an empty list, a private profile, and an unknown username",
                username);
            yield break;
        }

        if (items.Count >= MaxGaps)
        {
            _logger.LogWarning(
                "Trakt watchlist: stopped reading {User} at {Cap} entries; anything past that is not reported",
                username,
                MaxGaps);
        }

        _logger.LogInformation("Trakt watchlist: {User} wants {Count} titles", username, items.Count);
        context.ReportProgress(0.5);

        foreach (var gap in TraktListMapper.BuildWatchlist(username, items, context.Ownership, MaxGaps))
        {
            yield return gap;
        }

        context.ReportProgress(1);
    }
}
