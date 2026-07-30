namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// A title's kind. <see cref="CanHaveEpisodes"/> is what routes an entry to Shows or Movies: IMDb has a long
/// tail of kinds (movie, tvMovie, short, video, tvSpecial, tvSeries, tvMiniSeries), and this flag is the
/// schema's own answer to "is this episodic", so the plugin does not carry a list of kind strings to match.
/// </summary>
internal sealed class ImdbTitleType
{
    /// <summary>
    /// Gets or sets the kind id (for example "movie", "tvSeries", "tvMiniSeries").
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the kind is episodic.
    /// </summary>
    public bool CanHaveEpisodes { get; set; }
}
