using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Services.Acquisition;

/// <summary>
/// What Radarr and Sonarr already hold, by TMDB id.
/// </summary>
public sealed class ArrPresence
{
    /// <summary>
    /// Gets or sets Radarr's movies by TMDB id.
    /// </summary>
    public IReadOnlyDictionary<int, ArrItemState> Movies { get; set; } = new Dictionary<int, ArrItemState>();

    /// <summary>
    /// Gets or sets Sonarr's series by TMDB id.
    /// </summary>
    public IReadOnlyDictionary<int, ArrItemState> Series { get; set; } = new Dictionary<int, ArrItemState>();
}
