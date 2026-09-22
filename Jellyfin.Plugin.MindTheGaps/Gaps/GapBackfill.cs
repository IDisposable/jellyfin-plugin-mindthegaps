using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// The part of a scan that folds the previous report into the current one: re-adopting enrichment a
/// background pass resolved, and carrying forward a prior gap a capped or config-scoped source did not
/// re-emit this run. Pure and standalone, like <see cref="StaleOwnerPruner"/>: every decision here depends
/// only on the gaps, the ownership index, and the caller's own bookkeeping (a dismissed-id set, a domain
/// set), never on a live provider or the library, which is exactly what makes it unit-testable without a
/// scan. <see cref="GapEngine"/> is the only caller and owns anything that does need the library
/// (its own series-content backfill queries owned episodes directly, so it stays there instead of here).
/// </summary>
internal static class GapBackfill
{
    /// <summary>
    /// The default cap on how many prior gaps a single accumulate pass will carry forward, so a huge
    /// library cannot grow the report without limit.
    /// </summary>
    public const int DefaultMaxAccumulated = 50000;

    /// <summary>
    /// Re-adopts enrichment (resolved external ids, "where to watch" offers, the episode watch target) a
    /// background pass stored on a prior gap, for every gap this run reproduced under the same id. Done
    /// before the host link pass so any newly re-adopted ids produce their links.
    /// </summary>
    /// <param name="gaps">This run's gaps, mutated in place.</param>
    /// <param name="priorItems">The previous report's gaps.</param>
    public static void CarryForward(IReadOnlyList<GapItem> gaps, IReadOnlyList<GapItem> priorItems)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(priorItems);

        if (priorItems.Count == 0)
        {
            return;
        }

        var priorById = new Dictionary<string, GapItem>(priorItems.Count, StringComparer.Ordinal);
        foreach (var item in priorItems)
        {
            priorById[item.Id] = item;
        }

        foreach (var gap in gaps)
        {
            if (!priorById.TryGetValue(gap.Id, out var before))
            {
                continue;
            }

            // Re-adopt any external ids the background pass resolved last time (the sources only stamp
            // a TMDB id), and rebuild the fallback links the added ids imply.
            var merged = new Dictionary<string, string>(gap.ProviderIds, StringComparer.OrdinalIgnoreCase);
            var added = false;
            foreach (var pair in before.ProviderIds)
            {
                if (!string.IsNullOrEmpty(pair.Value) && !merged.ContainsKey(pair.Key))
                {
                    merged[pair.Key] = pair.Value;
                    added = true;
                }
            }

            if (added)
            {
                gap.ProviderIds = merged;
                gap.Links = ExternalLinkEnricher.Merge(gap.Links, ProviderLinks.Build(gap.TargetKind, merged));
            }

            // Carry the episode's watch target (its series' TMDB id, resolved by an earlier pass) so a
            // rescan does not drop it and re-resolve; sources only stamp it when they can.
            if (string.IsNullOrEmpty(gap.WatchTmdbId) && !string.IsNullOrEmpty(before.WatchTmdbId))
            {
                gap.WatchTmdbId = before.WatchTmdbId;
            }

            if (before.AvailabilityChecked)
            {
                gap.AvailabilityChecked = true;
            }

            if (gap.Availability.Count == 0 && before.Availability.Count > 0)
            {
                gap.Availability = before.Availability;
            }
        }
    }

    /// <summary>
    /// Carries forward prior gaps of one capped pattern (CreatorWorks, Recommendation) that this run did
    /// not re-emit: filmography and recommendation sources only scan a slice of their seeds each run, so
    /// coverage accumulates across runs instead of the un-scanned seeds' gaps vanishing. A gap whose source
    /// the user dismissed wholesale (<paramref name="dismissedSourceItemIds"/>) drains away instead.
    /// </summary>
    /// <param name="gaps">This run's gaps so far, appended to in place.</param>
    /// <param name="byId">This run's gaps by id, updated in place alongside <paramref name="gaps"/>.</param>
    /// <param name="prior">The previous report's gaps.</param>
    /// <param name="ownership">The current scan's ownership index.</param>
    /// <param name="pattern">The pattern to accumulate (CreatorWorks or Recommendation).</param>
    /// <param name="dismissedSourceItemIds">The <see cref="GapItem.SourceItemId"/> values dismissed wholesale for this pattern.</param>
    /// <param name="maxAccumulated">The cap on how many gaps of this pattern this call will carry in total.</param>
    /// <returns>How many gaps were carried, and whether the cap was reached before every eligible prior gap was considered.</returns>
    public static BackfillResult AccumulateUnowned(
        List<GapItem> gaps,
        Dictionary<string, GapItem> byId,
        IReadOnlyList<GapItem> prior,
        OwnershipIndex ownership,
        GapPattern pattern,
        IReadOnlySet<string> dismissedSourceItemIds,
        int maxAccumulated = DefaultMaxAccumulated)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(byId);
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(dismissedSourceItemIds);

        var count = gaps.Count(g => g.Pattern == pattern);
        var carried = 0;
        var cappedOut = false;
        foreach (var item in prior)
        {
            // Ad-hoc "explore" gaps are deliberately not carried forward: a scheduled scan clears any
            // exploration the user did not keep in config (a kept source re-produces them as permanent).
            if (item.Pattern != pattern || byId.ContainsKey(item.Id) || item.Adhoc)
            {
                continue;
            }

            if (item.SourceItemId is not null && dismissedSourceItemIds.Contains(item.SourceItemId))
            {
                continue;
            }

            if (ownership.OwnsAny(item.TargetKind, item.ProviderIds))
            {
                continue;
            }

            if (count >= maxAccumulated)
            {
                cappedOut = true;
                break;
            }

            byId[item.Id] = item;
            gaps.Add(item);
            count++;
            carried++;
        }

        return new BackfillResult(carried, cappedOut);
    }

    /// <summary>
    /// Carries forward prior set-completion gaps (collections, discographies) that this run did not
    /// re-emit and are still unowned, so a transient upstream failure mid-scan does not blank them from the
    /// saved report. These sources scan everything each run, so a clean run re-emits the live set (nothing
    /// extra is carried) and a later clean run drops anything truly resolved. Episode set-completion gaps
    /// are excluded: <see cref="GapEngine"/>'s own series-content backfill carries those, checking the
    /// library on disk directly.
    /// </summary>
    /// <param name="gaps">This run's gaps so far, appended to in place.</param>
    /// <param name="byId">This run's gaps by id, updated in place alongside <paramref name="gaps"/>.</param>
    /// <param name="prior">The previous report's gaps.</param>
    /// <param name="ownership">The current scan's ownership index.</param>
    /// <param name="domains">The domains whose set-completion source is enabled; a gap outside these domains is never carried.</param>
    /// <param name="maxAccumulated">The cap on how many gaps this call will carry in total.</param>
    /// <returns>How many gaps were carried, and whether the cap was reached before every eligible prior gap was considered.</returns>
    public static BackfillResult AccumulateSetCompletion(
        List<GapItem> gaps,
        Dictionary<string, GapItem> byId,
        IReadOnlyList<GapItem> prior,
        OwnershipIndex ownership,
        IReadOnlySet<MediaDomain> domains,
        int maxAccumulated = DefaultMaxAccumulated)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(byId);
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(domains);

        if (domains.Count == 0)
        {
            return new BackfillResult(0, false);
        }

        var carried = 0;
        var cappedOut = false;
        foreach (var item in prior)
        {
            if (item.Pattern != GapPattern.SetCompletion
                || item.TargetKind == BaseItemKind.Episode
                || item.Adhoc
                || byId.ContainsKey(item.Id)
                || !domains.Contains(item.Domain))
            {
                continue;
            }

            // Mirror how the sources decide ownership: a provider-id match, or for an album the artist-and-title
            // name key (a release the library holds under a different provider's id), so a now-owned item is not
            // wrongly resurrected.
            if (ownership.OwnsAny(item.TargetKind, item.ProviderIds)
                || (item.TargetKind == BaseItemKind.MusicAlbum && ownership.OwnsByName(item.TargetKind, item.SourceItemName, item.Name)))
            {
                continue;
            }

            if (carried >= maxAccumulated)
            {
                cappedOut = true;
                break;
            }

            byId[item.Id] = item;
            gaps.Add(item);
            carried++;
        }

        return new BackfillResult(carried, cappedOut);
    }

    /// <summary>
    /// The result of one accumulate pass.
    /// </summary>
    /// <param name="Carried">How many prior gaps were carried forward.</param>
    /// <param name="CappedOut">Whether the accumulation cap was reached before every eligible prior gap was considered.</param>
    public readonly record struct BackfillResult(int Carried, bool CappedOut);
}
