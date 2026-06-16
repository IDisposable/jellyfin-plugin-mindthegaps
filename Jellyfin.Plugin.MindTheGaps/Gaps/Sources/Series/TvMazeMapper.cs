using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.MindTheGaps.Services.TvMaze;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Maps TVmaze episode DTOs to the source-agnostic <see cref="CanonicalEpisode"/>.
/// </summary>
public static class TvMazeMapper
{
    /// <summary>
    /// Converts TVmaze episodes to canonical episodes, keeping only entries with a season and number.
    /// </summary>
    /// <param name="episodes">The TVmaze episodes.</param>
    /// <returns>The canonical episodes.</returns>
    public static IReadOnlyList<CanonicalEpisode> ToCanonical(IEnumerable<TvMazeEpisode> episodes)
    {
        var canonical = new List<CanonicalEpisode>();
        foreach (var episode in episodes)
        {
            if (episode.Season is int season && episode.Number is int number)
            {
                canonical.Add(new CanonicalEpisode(season, number, episode.Name, ParseDate(episode.Airdate), episode.Summary));
            }
        }

        return canonical;
    }

    private static DateTime? ParseDate(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
