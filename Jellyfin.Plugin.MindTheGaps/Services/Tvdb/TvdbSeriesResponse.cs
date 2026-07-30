namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// The envelope TheTVDB wraps a series record in.
/// </summary>
internal sealed class TvdbSeriesResponse
{
    /// <summary>Gets or sets the series record.</summary>
    public TvdbSeriesRecord? Data { get; set; }
}
