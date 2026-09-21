using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Groups owned episodes by series into the (season, number) pairs each series holds, so a caller that needs
/// the answer for many series reads the library's episodes once instead of once per series.
/// </summary>
internal static class OwnedEpisodeIndex
{
    /// <summary>
    /// Builds, for each wanted series, the set of (season, number) pairs its episodes cover. A series with no
    /// numbered episodes is absent from the result.
    /// </summary>
    /// <param name="episodes">Owned (non-virtual) library items; anything that is not an episode is ignored.</param>
    /// <param name="wanted">The series ids to keep; an episode of any other series is skipped.</param>
    /// <returns>The owned pairs per wanted series.</returns>
    public static Dictionary<Guid, HashSet<(int Season, int Number)>> BySeries(IEnumerable<BaseItem> episodes, IReadOnlySet<Guid> wanted)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(wanted);

        var bySeries = new Dictionary<Guid, HashSet<(int Season, int Number)>>();
        foreach (var item in episodes)
        {
            if (item is not Episode episode
                || !wanted.Contains(episode.SeriesId)
                || episode.ParentIndexNumber is not int season
                || episode.IndexNumber is not int number)
            {
                continue;
            }

            if (!bySeries.TryGetValue(episode.SeriesId, out var owned))
            {
                owned = [];
                bySeries[episode.SeriesId] = owned;
            }

            // One file can span several episodes (S01E01-E02), so own every number in the span; otherwise a
            // carried cross-check gap for the later part never drains even though the file is on disk.
            var last = episode.IndexNumberEnd is int end && end > number ? end : number;
            for (var n = number; n <= last; n++)
            {
                owned.Add((season, n));
            }
        }

        return bySeries;
    }
}
