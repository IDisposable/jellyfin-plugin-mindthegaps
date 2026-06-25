using System;
using System.Collections.Generic;
using TMDbLib.Objects.Search;

namespace Jellyfin.Plugin.MindTheGaps.Gaps.Sources.Series;

/// <summary>
/// Maps TheMovieDb season episodes (TMDbLib) to the canonical episode list the series-content diff uses.
/// Specials and unnumbered entries are carried through and dropped by the diff, as with the other mappers.
/// </summary>
internal static class TmdbSeriesMapper
{
    /// <summary>
    /// Maps TMDB season episodes to canonical episodes.
    /// </summary>
    /// <param name="episodes">The TMDB season episodes, across one or more seasons.</param>
    /// <returns>The canonical episodes.</returns>
    public static IReadOnlyList<CanonicalEpisode> ToCanonical(IEnumerable<TvSeasonEpisode> episodes)
    {
        ArgumentNullException.ThrowIfNull(episodes);

        var list = new List<CanonicalEpisode>();
        foreach (var ep in episodes)
        {
            list.Add(new CanonicalEpisode(ep.SeasonNumber, (int)ep.EpisodeNumber, ep.Name, ep.AirDate, ep.Overview));
        }

        return list;
    }
}
