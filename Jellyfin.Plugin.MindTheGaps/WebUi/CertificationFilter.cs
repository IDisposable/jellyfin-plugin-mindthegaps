using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Narrows what a user with a parental rating limit is shown on the web UI surfaces to what that limit allows
/// (<see cref="RatingGate"/>), looking each title's certification up on TMDB (cached, so a title costs one
/// request however often it is shown). Only movies and series are checked; an album or a book carries no
/// certification and is shown as it is. A title whose certification cannot be looked up is left out rather
/// than shown, since a lookup failure says nothing about what the title is.
/// </summary>
public sealed class CertificationFilter
{
    // A restricted user's first visit to a prolific person's page looks every credit up; a few at a time keeps
    // that well inside TMDB's rate limit without making the page wait on them one by one.
    private const int Parallelism = 4;

    private readonly TmdbClient _tmdb;
    private readonly ILibraryManager _library;
    private readonly ILogger<CertificationFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificationFilter"/> class.
    /// </summary>
    /// <param name="tmdb">The TMDB client, for each title's certification.</param>
    /// <param name="library">The library manager, for a card that is a title the library holds.</param>
    /// <param name="logger">The logger.</param>
    public CertificationFilter(TmdbClient tmdb, ILibraryManager library, ILogger<CertificationFilter> logger)
    {
        _tmdb = tmdb;
        _library = library;
        _logger = logger;
    }

    /// <summary>
    /// Keeps the cards the user's limit allows, in their order. A card for a title the library holds goes by
    /// the library's own rating for it.
    /// </summary>
    /// <param name="user">The user, who has a parental rating limit.</param>
    /// <param name="titles">The cards.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cards the user may be shown.</returns>
    public async Task<IReadOnlyList<MissingTitle>> AllowedAsync(User user, IReadOnlyList<MissingTitle> titles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(titles);

        var allowed = new bool[titles.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, titles.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
            async (i, ct) =>
            {
                var title = titles[i];
                allowed[i] = title.ItemId is { } itemId
                    ? _library.GetItemById(itemId) is BaseItem item && item.IsVisible(user)
                    : await AllowsAsync(user, Kind(title.Kind), title.TmdbId, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return titles.Where((_, i) => allowed[i]).ToList();
    }

    /// <summary>
    /// Determines whether the user's limit allows a gap's title, for an add to their want-to-watch list.
    /// </summary>
    /// <param name="user">The user, who has a parental rating limit.</param>
    /// <param name="gap">The gap.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when it may be shown.</returns>
    public Task<bool> AllowsAsync(User user, GapItem gap, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gap);

        return AllowsAsync(
            user,
            gap.TargetKind,
            gap.ProviderIds.TryGetProviderIdAsInt(ProviderIds.Tmdb, out var tmdbId) ? tmdbId : 0,
            cancellationToken);
    }

    /// <summary>
    /// Determines whether the user's limit allows a title.
    /// </summary>
    /// <param name="user">The user, who has a parental rating limit.</param>
    /// <param name="kind">The title's kind; anything but a movie or series is shown as it is.</param>
    /// <param name="tmdbId">The title's TMDB id, or 0 when it has none, which reads as unrated.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when it may be shown.</returns>
    public async Task<bool> AllowsAsync(User user, BaseItemKind kind, int tmdbId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (kind is not (BaseItemKind.Movie or BaseItemKind.Series))
        {
            return true;
        }

        var isSeries = kind == BaseItemKind.Series;
        if (tmdbId <= 0)
        {
            return RatingGate.Allows(user, isSeries, null);
        }

        try
        {
            var config = Plugin.Instance?.Configuration;
            var certification = await _tmdb.GetCertificationAsync(isSeries, tmdbId, config?.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
            return RatingGate.Allows(user, isSeries, certification);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not look up the certification of TMDB {Kind} {TmdbId}; leaving it out for a restricted user", kind, tmdbId);
            return false;
        }
    }

    private static BaseItemKind Kind(string kind)
        => Enum.TryParse<BaseItemKind>(kind, ignoreCase: true, out var parsed) ? parsed : BaseItemKind.Movie;
}
