namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// One row in a gap diagnosis: either the gap itself (the missing title) or an owned item that looks like
/// it should be the gap. Carries the provider ids so the dashboard can link each out and compare them.
/// </summary>
public sealed class DiagnosisItem
{
    /// <summary>
    /// Gets or sets how this row relates to the gap: "target" (the gap itself), "titleMatch" (an owned
    /// item with the same title), or "idHolder" (an owned item already carrying the gap's id).
    /// </summary>
    public string Relation { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the year, if known.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the TheMovieDb id, if known.
    /// </summary>
    public string? Tmdb { get; set; }

    /// <summary>
    /// Gets or sets the IMDb id, if known.
    /// </summary>
    public string? Imdb { get; set; }

    /// <summary>
    /// Gets or sets the TheTVDB id, if known.
    /// </summary>
    public string? Tvdb { get; set; }

    /// <summary>
    /// Gets or sets the owned item's id (N-format guid) for an "open in Jellyfin" jump; null for the gap.
    /// </summary>
    public string? JellyfinItemId { get; set; }

    /// <summary>
    /// Gets or sets a short note on this row (for example "no TheMovieDb id" or "probably misidentified").
    /// </summary>
    public string? Note { get; set; }
}
