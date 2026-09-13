using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Rehydrates a card's gap from the surface it came from, and describes it for the detail dialog. One place
/// for the "never trust a client-posted gap" rule across every web UI surface.
/// </summary>
public sealed class WebUiGapResolver
{
    private readonly PersonMissingService _person;
    private readonly RelatedMissingService _related;
    private readonly HomeDiscoverService _home;
    private readonly WantToWatchService _want;
    private readonly TmdbClient _tmdb;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebUiGapResolver"/> class.
    /// </summary>
    /// <param name="person">The person page's lookup.</param>
    /// <param name="related">The item page's lookup.</param>
    /// <param name="home">The home row's lookup.</param>
    /// <param name="want">The want-to-watch list's lookup.</param>
    /// <param name="tmdb">The TMDB client, for the detail record.</param>
    public WebUiGapResolver(PersonMissingService person, RelatedMissingService related, HomeDiscoverService home, WantToWatchService want, TmdbClient tmdb)
    {
        _person = person;
        _related = related;
        _home = home;
        _want = want;
        _tmdb = tmdb;
    }

    /// <summary>
    /// Rehydrates the gap a card showed.
    /// </summary>
    /// <param name="source">The surface.</param>
    /// <param name="sourceId">The person or item id the surface was for; ignored for the home row.</param>
    /// <param name="gapId">The gap id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The gap, or <see langword="null"/> when the surface does not currently show it.</returns>
    public async Task<GapItem?> ResolveAsync(GapSource source, Guid sourceId, string gapId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(gapId))
        {
            return null;
        }

        return source switch
        {
            GapSource.Person => await _person.FindGapAsync(sourceId, gapId, cancellationToken).ConfigureAwait(false),
            GapSource.Item => await _related.FindGapAsync(sourceId, gapId, cancellationToken).ConfigureAwait(false),
            GapSource.Home => _home.FindGap(gapId),
            GapSource.Todo => _want.FindGap(gapId),
            _ => null
        };
    }

    /// <summary>
    /// Describes a card's title from TMDB's own record, for the detail dialog.
    /// </summary>
    /// <param name="source">The surface.</param>
    /// <param name="sourceId">The person or item id the surface was for; ignored for the home row.</param>
    /// <param name="gapId">The gap id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The detail, or <see langword="null"/> when the gap or TMDB's record is not there.</returns>
    public async Task<MissingTitleDetail?> DescribeAsync(GapSource source, Guid sourceId, string gapId, CancellationToken cancellationToken)
    {
        var gap = await ResolveAsync(source, sourceId, gapId, cancellationToken).ConfigureAwait(false);
        if (gap is null || MissingTitleBuilder.TmdbId(gap) is not int tmdbId)
        {
            return null;
        }

        // A filmography gap's overview is the person's credit; a recommendation's is TMDB's synopsis, which
        // the detail fetch replaces anyway. Only the former is a "credit" line.
        var role = source == GapSource.Person ? gap.Overview : null;
        var because = source == GapSource.Person ? null : MissingTitleBuilder.Because(gap);

        var config = Plugin.RequireConfiguration();
        if (gap.TargetKind == BaseItemKind.Movie)
        {
            var movie = await _tmdb.GetMovieDetailsAsync(tmdbId, config.MetadataLanguage, config.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
            return movie is null ? null : MissingTitleDetailMapper.FromMovie(gap, movie, role, because, _tmdb.GetPosterUrl, _tmdb.GetBackdropUrl);
        }

        var show = await _tmdb.GetSeriesDetailsAsync(tmdbId, config.MetadataLanguage, config.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
        return show is null ? null : MissingTitleDetailMapper.FromSeries(gap, show, role, because, _tmdb.GetPosterUrl, _tmdb.GetBackdropUrl);
    }

    /// <summary>
    /// Parses a surface name from a query string.
    /// </summary>
    /// <param name="value">The value ("person", "item", "home", "todo").</param>
    /// <param name="source">The parsed surface.</param>
    /// <returns><see langword="true"/> when recognised.</returns>
    public static bool TryParse(string? value, out GapSource source)
    {
        // Names only: Enum.TryParse also accepts a bare number ("2"), which is not a surface a client names.
        if (!string.IsNullOrEmpty(value)
            && !char.IsDigit(value[0])
            && value[0] != '-'
            && Enum.TryParse(value, ignoreCase: true, out source)
            && Enum.IsDefined(source))
        {
            return true;
        }

        source = default;
        return false;
    }

    /// <summary>
    /// Formats a surface name for a client.
    /// </summary>
    /// <param name="source">The surface.</param>
    /// <returns>The lower-case name.</returns>
    public static string Name(GapSource source) => source.ToString().ToLowerInvariant();

    /// <summary>
    /// Formats an id the way the surfaces key on it.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>The "N" form.</returns>
    public static string Key(Guid id) => id.ToString("N", CultureInfo.InvariantCulture);
}
