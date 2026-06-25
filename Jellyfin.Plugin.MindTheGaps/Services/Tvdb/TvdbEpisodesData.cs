using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// The data payload of a TheTVDB episodes response.
/// </summary>
internal class TvdbEpisodesData
{
    /// <summary>Gets or sets the episodes for the requested page.</summary>
    public IReadOnlyList<TvdbEpisode>? Episodes { get; set; }
}
