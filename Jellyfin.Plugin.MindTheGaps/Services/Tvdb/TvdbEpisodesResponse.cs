namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// A TheTVDB <c>/series/{id}/episodes</c> response page.
/// </summary>
public class TvdbEpisodesResponse
{
    /// <summary>Gets or sets the data payload.</summary>
    public TvdbEpisodesData? Data { get; set; }

    /// <summary>Gets or sets the pagination links.</summary>
    public TvdbLinks? Links { get; set; }
}
