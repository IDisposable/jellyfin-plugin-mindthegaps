namespace Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

/// <summary>
/// A TheTVDB episode record.
/// </summary>
internal class TvdbEpisode
{
    /// <summary>Gets or sets the TheTVDB episode id.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the season number.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number within the season.</summary>
    public int? Number { get; set; }

    /// <summary>Gets or sets the episode title.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the original air date (yyyy-MM-dd).</summary>
    public string? Aired { get; set; }

    /// <summary>Gets or sets the overview.</summary>
    public string? Overview { get; set; }
}
