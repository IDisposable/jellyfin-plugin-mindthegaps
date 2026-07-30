namespace Jellyfin.Plugin.MindTheGaps.Services.JustWatch;

/// <summary>
/// The localized detail of a title, as returned for the requested country and language.
/// </summary>
internal sealed class JustWatchContent
{
    /// <summary>
    /// Gets or sets the display title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the original release year.
    /// </summary>
    public int? OriginalReleaseYear { get; set; }

    /// <summary>
    /// Gets or sets the title's path on justwatch.com, for the "where this came from" link.
    /// </summary>
    public string? FullPath { get; set; }

    /// <summary>
    /// Gets or sets the poster path. It is a template holding {profile} and {format} placeholders rather than
    /// a URL, so it is expanded before use.
    /// </summary>
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Gets or sets the external ids.
    /// </summary>
    public JustWatchExternalIds? ExternalIds { get; set; }
}
