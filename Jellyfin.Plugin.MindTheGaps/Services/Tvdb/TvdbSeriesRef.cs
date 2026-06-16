namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// A reference to a TheTVDB series (as returned by remote-id search).
/// </summary>
public class TvdbSeriesRef
{
    /// <summary>Gets or sets the TheTVDB series id.</summary>
    public long Id { get; set; }
}
