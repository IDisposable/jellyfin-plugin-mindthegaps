using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A series that holds one season number in more than one folder (the "Season 1" and "Season 01" footgun).
/// However the episodes fall out across the folders (two full copies, the episodes scattered between them, or
/// one folder holding only extras), a season number split across folders is wrong, because the library can no
/// longer present that season as one run, so a tool reading its episodes sees a broken or empty season.
/// </summary>
public sealed class DuplicateSeasonGroup
{
    /// <summary>
    /// Gets or sets the owning series' name.
    /// </summary>
    public string SeriesName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the owning series' id (N-format guid) for an "open in Jellyfin" jump.
    /// </summary>
    public string SeriesJellyfinItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the season number that is claimed by more than one folder.
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the folders that claim this season number (two or more).
    /// </summary>
    public IReadOnlyList<DuplicateSeasonFolder> Folders { get; set; } = [];
}
