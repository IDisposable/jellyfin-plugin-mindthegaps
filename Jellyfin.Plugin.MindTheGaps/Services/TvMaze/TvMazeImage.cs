namespace Jellyfin.Plugin.MindTheGaps.Services.TvMaze;

/// <summary>
/// A TVmaze image reference (a show's own image, or an episode still).
/// </summary>
internal class TvMazeImage
{
    /// <summary>Gets or sets the medium-sized image URL.</summary>
    public string? Medium { get; set; }

    /// <summary>Gets or sets the original, full-size image URL.</summary>
    public string? Original { get; set; }
}
