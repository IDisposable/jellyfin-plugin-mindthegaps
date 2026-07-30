namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// A series record, as much of it as a favourite needs to become a report row.
/// </summary>
internal sealed class TvdbSeriesRecord
{
    /// <summary>Gets or sets TheTVDB series id.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the series name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the poster URL.</summary>
    public string? Image { get; set; }

    /// <summary>Gets or sets the first air date ("1950-05-13").</summary>
    public string? FirstAired { get; set; }
}
