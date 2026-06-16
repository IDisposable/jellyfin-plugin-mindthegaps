using System;
using System.Globalization;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Builds the stable gap id for a missing episode. Shared by every series-content source (the
/// library reader and the TVmaze/TheTVDB cross-checks) so the engine de-dupes the same missing
/// episode regardless of which source surfaced it.
/// </summary>
public static class SeriesGapKey
{
    /// <summary>
    /// Builds the gap id for a missing episode, anchored on the owned series and its season/episode
    /// number so all sources agree on the same key.
    /// </summary>
    /// <param name="seriesId">The Jellyfin id of the owned series.</param>
    /// <param name="season">The season number.</param>
    /// <param name="number">The episode number within the season.</param>
    /// <returns>A stable de-dup id.</returns>
    public static string Episode(Guid seriesId, int season, int number)
        => string.Create(CultureInfo.InvariantCulture, $"seriescontent:{seriesId:N}:s{season:D2}e{number:D2}");
}
