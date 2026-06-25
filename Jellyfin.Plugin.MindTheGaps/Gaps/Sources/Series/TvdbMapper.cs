using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.MindTheGaps.Services.Tvdb;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Maps TheTVDB episode records to the source-agnostic <see cref="CanonicalEpisode"/>.
/// </summary>
internal static class TvdbMapper
{
    /// <summary>
    /// Converts TheTVDB episodes to canonical episodes, keeping only entries with a season and number.
    /// </summary>
    /// <param name="episodes">The TheTVDB episodes.</param>
    /// <returns>The canonical episodes.</returns>
    public static IReadOnlyList<CanonicalEpisode> ToCanonical(IEnumerable<TvdbEpisode> episodes)
    {
        var canonical = new List<CanonicalEpisode>();
        foreach (var episode in episodes)
        {
            if (episode.SeasonNumber is int season && episode.Number is int number)
            {
                canonical.Add(new CanonicalEpisode(season, number, episode.Name, ParseDate(episode.Aired), episode.Overview));
            }
        }

        return canonical;
    }

    private static DateTime? ParseDate(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
