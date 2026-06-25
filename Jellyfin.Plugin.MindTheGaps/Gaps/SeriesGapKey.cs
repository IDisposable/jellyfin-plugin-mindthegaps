using System;
using System.Globalization;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Builds the stable gap id for a missing episode. Shared by every series-content source (the
/// library reader and the TVmaze/TheTVDB cross-checks) so the engine de-dupes the same missing
/// episode regardless of which source surfaced it.
/// </summary>
internal static class SeriesGapKey
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

    /// <summary>
    /// Parses the season and episode number back out of an id built by <see cref="Episode"/>. Returns
    /// false for any other id shape (including the library reader's episode-id fallback form, which has
    /// no season/number), so callers can safely probe an arbitrary gap id.
    /// </summary>
    /// <param name="id">The gap id to parse.</param>
    /// <param name="season">The parsed season number.</param>
    /// <param name="number">The parsed episode number.</param>
    /// <returns><see langword="true"/> if the id is a season/episode key.</returns>
    public static bool TryParseEpisode(string id, out int season, out int number)
    {
        season = 0;
        number = 0;
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        var lastColon = id.LastIndexOf(':');
        if (lastColon < 0 || lastColon + 1 >= id.Length)
        {
            return false;
        }

        var code = id.AsSpan(lastColon + 1);
        if (code.Length < 4 || (code[0] != 's' && code[0] != 'S'))
        {
            return false;
        }

        var eIndex = -1;
        for (var i = 1; i < code.Length; i++)
        {
            if (code[i] == 'e' || code[i] == 'E')
            {
                eIndex = i;
                break;
            }
        }

        if (eIndex < 2 || eIndex + 1 >= code.Length)
        {
            return false;
        }

        return int.TryParse(code.Slice(1, eIndex - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out season)
            && int.TryParse(code[(eIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }
}
