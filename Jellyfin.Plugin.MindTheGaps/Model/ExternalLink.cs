namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A named external link for a gap (TMDB, JustWatch, Trakt, ...).
/// </summary>
public class ExternalLink
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalLink"/> class.
    /// </summary>
    public ExternalLink()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalLink"/> class.
    /// </summary>
    /// <param name="name">The link display name.</param>
    /// <param name="url">The target URL.</param>
    public ExternalLink(string name, string url)
    {
        Name = name;
        Url = url;
    }

    /// <summary>
    /// Gets or sets the display name (e.g. "TMDB").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;
}
