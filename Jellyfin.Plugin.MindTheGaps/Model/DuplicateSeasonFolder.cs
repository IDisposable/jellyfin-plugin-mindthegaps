namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// One folder behind a duplicate-season finding: a single <c>Season</c> entity the library built for a season
/// number that more than one folder claims. Carries the folder path and its episode count so the reader can
/// tell which copy holds the episodes, which holds only extras, and which to merge or delete.
/// </summary>
public sealed class DuplicateSeasonFolder
{
    /// <summary>
    /// Gets or sets the season entity's name (for example "Season 1" or "Season 01").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the folder path on disk, if the library exposes one. This is what tells two same-numbered
    /// seasons apart, so the reader can find the duplicate folders to merge.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the season's id (N-format guid) for an "open in Jellyfin" jump.
    /// </summary>
    public string JellyfinItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how many non-virtual episodes this folder holds.
    /// </summary>
    public int EpisodeCount { get; set; }
}
