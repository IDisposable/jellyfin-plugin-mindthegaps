namespace Jellyfin.Plugin.MindTheGaps.Services.Trakt;

/// <summary>
/// Cross-provider id block returned by Trakt.
/// </summary>
public class TraktIds
{
    /// <summary>Gets or sets the numeric Trakt id.</summary>
    public int? Trakt { get; set; }

    /// <summary>Gets or sets the Trakt slug.</summary>
    public string? Slug { get; set; }

    /// <summary>Gets or sets the IMDb id.</summary>
    public string? Imdb { get; set; }

    /// <summary>Gets or sets the TMDB id.</summary>
    public int? Tmdb { get; set; }
}
