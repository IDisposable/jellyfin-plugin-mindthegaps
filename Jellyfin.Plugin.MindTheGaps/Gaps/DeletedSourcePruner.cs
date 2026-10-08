using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Drops the sources of a gap that name library items the library no longer holds. A deleted movie cannot go
/// on recommending a title, and a deleted collection or series cannot go on being incomplete, but a rotating
/// source carries its earlier gaps forward until it next reaches that owner, which for a deleted owner is
/// never. Pure and standalone, like <see cref="StaleOwnerPruner"/>, so the decision is unit-testable without
/// a library: the caller looks the ids up and hands over the ones that are gone.
/// </summary>
internal static class DeletedSourcePruner
{
    /// <summary>
    /// Collects every source id across the gaps that names a library item (an N-format guid), primary and
    /// secondary alike. The other owners (a list, an account, an IMDb person) are not library items and so
    /// cannot have been deleted from it.
    /// </summary>
    /// <param name="items">The gaps to read.</param>
    /// <returns>The distinct library item ids, parsed.</returns>
    public static IReadOnlySet<Guid> LibrarySourceIds(IEnumerable<GapItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var ids = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (TryLibraryId(item.SourceItemId, out var primary))
            {
                ids.Add(primary);
            }

            foreach (var other in item.OtherSources ?? [])
            {
                if (TryLibraryId(other.Id, out var id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Applies the deleted sources to one gap. A deleted secondary source is dropped; a deleted primary source
    /// is replaced by the first secondary still standing; a gap left with no source at all is removed.
    /// </summary>
    /// <param name="item">The gap, which is not changed.</param>
    /// <param name="deleted">The library item ids that are gone, in the same N format the gaps carry.</param>
    /// <returns>The same gap when nothing it names is gone, an edited copy when a source was dropped, or null
    /// when the gap should be removed.</returns>
    public static GapItem? Prune(GapItem item, IReadOnlySet<string> deleted)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(deleted);

        var primaryGone = IsDeleted(item.SourceItemId, deleted);
        var others = item.OtherSources ?? [];
        var kept = new List<GapSourceRef>(others.Count);
        foreach (var other in others)
        {
            if (!IsDeleted(other.Id, deleted))
            {
                kept.Add(other);
            }
        }

        if (!primaryGone && kept.Count == others.Count)
        {
            return item;
        }

        if (primaryGone && kept.Count == 0)
        {
            return null;
        }

        var copy = item.ShallowCopy();
        if (primaryGone)
        {
            var promoted = kept[0];
            kept.RemoveAt(0);
            copy.SourceItemId = promoted.Id;
            copy.SourceItemName = promoted.Name;
            copy.SourceItemType = promoted.Type;
            copy.SourceItemYear = promoted.Year;
            copy.SourceProviderIds = promoted.ProviderIds;

            // Empty for a secondary source stored without its ids, until the next scan that reaches it.
            copy.SourceLinks = CreatorLinks.Build(promoted.Type, promoted.ProviderIds);
        }

        copy.OtherSources = kept.Count == 0 ? null : kept;
        return copy;
    }

    private static bool IsDeleted(string? id, IReadOnlySet<string> deleted)
        => !string.IsNullOrEmpty(id) && deleted.Contains(id);

    private static bool TryLibraryId(string? id, out Guid parsed)
    {
        parsed = Guid.Empty;
        return id is not null && Guid.TryParseExact(id, "N", out parsed) && parsed != Guid.Empty;
    }
}
