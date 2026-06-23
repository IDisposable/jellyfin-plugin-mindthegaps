using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Folds the sources of two gaps that collapsed to one id (the same missing title surfaced more than once)
/// onto the survivor, so the report can list every source that points at the title. A curated list the user
/// deliberately added (a TMDB list or an MDBList list) outranks an algorithmic per-title recommendation for
/// the primary, grouping source, so a list's titles collapse under the list rather than scattering under the
/// owned titles that also recommend them; the demoted recommendation rides along as a secondary source.
/// </summary>
public static class GapSourceMerge
{
    // Cap how many secondary sources ride along on one gap, so a very popular title does not grow unbounded.
    private const int MaxSourcesPerGap = 12;

    // The source types of the deliberately-curated discovery lists: a TMDB list (CuratedSetGapSource emits
    // "List") and an MDBList list ("MdbList"). These outrank a per-title recommendation (source type "Movie"
    // or "Series") for the primary source.
    private static readonly HashSet<string> CuratedListSourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "List",
        "MdbList"
    };

    /// <summary>
    /// Folds a duplicate gap's source into the surviving gap. Only recommendation gaps accumulate sources
    /// this way; when the duplicate comes from a curated list and the survivor from a per-title
    /// recommendation, the list is promoted to the primary source and the recommendation is demoted to a
    /// secondary "also from" source.
    /// </summary>
    /// <param name="existing">The surviving gap, mutated in place.</param>
    /// <param name="duplicate">The duplicate gap whose source is folded in.</param>
    public static void Merge(GapItem existing, GapItem duplicate)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(duplicate);

        if (existing.Pattern != GapPattern.Recommendation || string.IsNullOrEmpty(duplicate.SourceItemName))
        {
            return;
        }

        if (IsCuratedListSource(duplicate) && !IsCuratedListSource(existing))
        {
            // Promote the list to primary and demote the recommendation the title already had to a secondary.
            var demoted = ToSourceRef(existing);
            existing.SourceItemId = duplicate.SourceItemId;
            existing.SourceItemName = duplicate.SourceItemName;
            existing.SourceItemType = duplicate.SourceItemType;
            existing.SourceItemYear = duplicate.SourceItemYear;
            existing.SourceLinks = duplicate.SourceLinks;
            AddOtherSource(existing, demoted);
            return;
        }

        AddOtherSource(existing, ToSourceRef(duplicate));
    }

    private static bool IsCuratedListSource(GapItem gap)
        => gap.SourceItemType is { } type && CuratedListSourceTypes.Contains(type);

    private static GapSourceRef ToSourceRef(GapItem gap) => new()
    {
        Id = gap.SourceItemId,
        Name = gap.SourceItemName,
        Type = gap.SourceItemType,
        Year = gap.SourceItemYear
    };

    private static void AddOtherSource(GapItem existing, GapSourceRef add)
    {
        if (string.IsNullOrEmpty(add.Name))
        {
            return;
        }

        if (string.Equals(add.Name, existing.SourceItemName, StringComparison.Ordinal)
            && string.Equals(add.Id, existing.SourceItemId, StringComparison.Ordinal))
        {
            return;
        }

        var current = existing.OtherSources ?? (IReadOnlyList<GapSourceRef>)Array.Empty<GapSourceRef>();
        if (current.Count >= MaxSourcesPerGap)
        {
            return;
        }

        var key = add.Id ?? add.Name;
        foreach (var existingSource in current)
        {
            if (string.Equals(existingSource.Id ?? existingSource.Name, key, StringComparison.Ordinal))
            {
                return;
            }
        }

        existing.OtherSources = new List<GapSourceRef>(current) { add };
    }
}
