namespace Jellyfin.Plugin.MindTheGaps.Services.TvMaze;

/// <summary>
/// A TVmaze episode.
/// </summary>
internal class TvMazeEpisode
{
    /// <summary>Gets or sets the TVmaze episode id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the season number.</summary>
    public int? Season { get; set; }

    /// <summary>Gets or sets the episode number within the season.</summary>
    public int? Number { get; set; }

    /// <summary>Gets or sets the episode title.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the original air date (yyyy-MM-dd).</summary>
    public string? Airdate { get; set; }

    /// <summary>Gets or sets the summary (may contain HTML).</summary>
    public string? Summary { get; set; }
}
