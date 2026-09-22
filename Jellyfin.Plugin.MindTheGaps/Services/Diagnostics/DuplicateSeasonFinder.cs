using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Services.Diagnostics;

/// <summary>
/// Finds a season number more than one folder claims (a "Season 1" and a "Season 01" both mapping to
/// season 1), which splits or hides that season's episodes and is a common cause of a series reading as
/// missing everything. Pure and standalone, like <see cref="TitleIdentityDiagnosis"/>:
/// <see cref="GapDiagnostics"/> is the only caller, and owns reading the seasons from the library.
/// </summary>
internal static class DuplicateSeasonFinder
{
    // Group the owned seasons by series and season number, and flag any number that more than one folder
    // claims. A duplicate season number is wrong regardless of how its episodes fall out (two full copies, the
    // episodes scattered across both folders, or one folder holding only extras), so the test is the count of
    // folders per number, not their episodes; the episode counts ride along only so the reader can see which
    // folder to keep. The testable seam; GapDiagnostics.BuildAudit gathers the seasons from the library.
    public static IReadOnlyList<DuplicateSeasonGroup> FindDuplicateSeasons(IReadOnlyList<SeasonInfo> seasons)
    {
        var groups = new List<DuplicateSeasonGroup>();
        foreach (var bySeries in seasons.Where(s => s.Number.HasValue).GroupBy(s => s.SeriesId, StringComparer.Ordinal))
        {
            foreach (var byNumber in bySeries.GroupBy(s => s.Number!.Value).OrderBy(g => g.Key))
            {
                var folders = byNumber.ToList();
                if (folders.Count < 2)
                {
                    continue;
                }

                groups.Add(new DuplicateSeasonGroup
                {
                    SeriesName = folders[0].SeriesName,
                    SeriesJellyfinItemId = folders[0].SeriesId,
                    SeasonNumber = byNumber.Key,
                    Folders = folders.Select(f => new DuplicateSeasonFolder
                    {
                        Name = f.SeasonName,
                        Path = f.Path,
                        JellyfinItemId = f.SeasonId,
                        EpisodeCount = f.EpisodeCount
                    }).ToList()
                });
            }
        }

        return groups;
    }

    // One owned season for the duplicate-season audit: its series, its number, and the display fields the
    // finding carries through (the season's name, folder path, id, and episode count).
    internal readonly record struct SeasonInfo(string SeriesName, string SeriesId, int? Number, string SeasonName, string? Path, string SeasonId, int EpisodeCount);
}
