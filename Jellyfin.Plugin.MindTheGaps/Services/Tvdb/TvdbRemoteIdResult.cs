namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// One result of a TheTVDB remote-id search.
/// </summary>
internal class TvdbRemoteIdResult
{
    /// <summary>Gets or sets the matched series, if the result is a series.</summary>
    public TvdbSeriesRef? Series { get; set; }
}
