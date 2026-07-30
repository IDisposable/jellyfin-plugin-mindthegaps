using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// The identity of the thing a gap is about, as opposed to <see cref="GapItem.Id"/>, which identifies the
/// gap. One missing title is normally several gaps: Mad Max 2 absent from the library is a hole in its
/// collection, in a studio set, in its director's filmography, and a recommendation, each with its own id
/// from its own source. Acquiring the film fills all of them at once, so the report's verify uses these keys
/// to drop every gap about a title it just confirmed you own, not only the row that was clicked.
/// </summary>
/// <remarks>
/// Keys are built the same way <see cref="OwnershipIndex"/> keys the library, so "the same title" means here
/// exactly what it means when a source decides you own something: a shared provider id under the same item
/// kind, or for an album the artist-and-title name key, which is what matches a Discogs release against a
/// MusicBrainz-tagged one.
/// </remarks>
internal static class GapTargetKey
{
    /// <summary>
    /// Builds the identity keys for a gap. A gap carrying several provider ids yields one key per id, so two
    /// gaps match when they agree on any single provider, which is what lets a TMDB-only row match one that
    /// has since had its IMDb id resolved.
    /// </summary>
    /// <param name="gap">The gap.</param>
    /// <returns>Its identity keys, empty when it carries nothing to match on.</returns>
    public static IEnumerable<string> For(GapItem gap)
    {
        if (gap is null)
        {
            yield break;
        }

        foreach (var pair in gap.ProviderIds)
        {
            if (!string.IsNullOrEmpty(pair.Value))
            {
                yield return OwnershipIndex.MakeKey(gap.TargetKind, pair.Key, pair.Value);
            }
        }

        // The album name fallback, matching LibraryVerifier: album sources often share no provider id, so
        // without this a Discogs row would survive clearing the MusicBrainz one for the same record.
        if (gap.TargetKind == BaseItemKind.MusicAlbum && !string.IsNullOrEmpty(gap.Name))
        {
            yield return OwnershipIndex.MakeKey(
                gap.TargetKind,
                OwnershipIndex.NameKeyProvider,
                OwnershipIndex.NameKey(gap.SourceItemName, gap.Name));
        }
    }

    /// <summary>
    /// Finds every gap in a report that is about one of the given titles, including the titles themselves.
    /// </summary>
    /// <remarks>
    /// Matching is transitive, because a title's rows do not all carry the same ids: the background pass
    /// resolves an IMDb id onto some and not others. A TMDB-only row, a TMDB-and-IMDb row, and an IMDb-only
    /// row are one film, but the first and last share no key directly. So this walks the graph of gaps and
    /// the ids they share, which makes clearing any one of them clear all of them rather than only the ones
    /// the clicked row happened to overlap. Ids are returned in the order the walk reaches them, not in
    /// report order.
    /// </remarks>
    /// <param name="items">The report's gaps. Taken as a collection so the index can be sized up front.</param>
    /// <param name="targets">The gaps whose titles have been confirmed owned.</param>
    /// <returns>The ids of every gap about those titles.</returns>
    public static IReadOnlyList<string> MatchingIds(IReadOnlyCollection<GapItem> items, IEnumerable<GapItem> targets)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(targets);

        // Invert the report once, key to the gaps carrying it. The walk below then follows keys to gaps and
        // on to those gaps' other keys, so each key and each gap is visited at most once; matching by
        // re-scanning every gap on each pass would cost a full sweep per link in the chain.
        // Sized by the gap count: most gaps carry one or two ids, so this lands within a growth step or
        // two of the real key count rather than resizing its way up from nothing on a large report.
        var byKey = new Dictionary<string, List<GapItem>>(items.Count, StringComparer.Ordinal);
        foreach (var item in items)
        {
            foreach (var key in For(item))
            {
                if (!byKey.TryGetValue(key, out var carriers))
                {
                    carriers = [];
                    byKey[key] = carriers;
                }

                carriers.Add(item);
            }
        }

        // Seed the walk with the confirmed titles' own keys.
        var pending = new Queue<string>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            foreach (var key in For(target))
            {
                if (seenKeys.Add(key))
                {
                    pending.Enqueue(key);
                }
            }
        }

        // Deliberately not presized: what comes back is one title's rows, a handful, however big the report
        // is. Sizing these to the report would allocate for tens of thousands to hold five.
        var matched = new List<string>();
        var takenIds = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            if (!byKey.TryGetValue(pending.Dequeue(), out var carriers))
            {
                continue;
            }

            foreach (var item in carriers)
            {
                if (!takenIds.Add(item.Id))
                {
                    continue;
                }

                matched.Add(item.Id);

                // Adopt this gap's other ids, which is what reaches a row sharing only one of them.
                foreach (var other in For(item))
                {
                    if (seenKeys.Add(other))
                    {
                        pending.Enqueue(other);
                    }
                }
            }
        }

        return matched;
    }
}
