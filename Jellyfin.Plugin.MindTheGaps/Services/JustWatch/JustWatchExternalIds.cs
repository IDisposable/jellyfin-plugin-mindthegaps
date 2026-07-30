namespace Jellyfin.Plugin.MindTheGaps.Services.JustWatch;

/// <summary>
/// The external ids JustWatch records for a title, which is what lets a watchlist entry be diffed against the
/// library without a title match.
/// </summary>
internal sealed class JustWatchExternalIds
{
    /// <summary>
    /// Gets or sets the IMDb id ("tt0133093").
    /// </summary>
    public string? ImdbId { get; set; }

    /// <summary>
    /// Gets or sets TheMovieDb id, as a string.
    /// </summary>
    public string? TmdbId { get; set; }
}
