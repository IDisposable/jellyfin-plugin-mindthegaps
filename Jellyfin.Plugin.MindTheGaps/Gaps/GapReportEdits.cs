using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// The pure editing logic behind <see cref="GapStore"/>'s partial-update methods (a re-check swap, an
/// availability merge, an ad-hoc explore's carried enrichment): each depends only on the reports or gaps
/// handed in, never on the store's cached state or its lock, which is what makes these unit-testable
/// without a store, the same reason <see cref="StaleOwnerPruner"/> and <see cref="GapBackfill"/> are.
/// </summary>
internal static class GapReportEdits
{
    /// <summary>
    /// Whether a gap is the one a re-check is replacing: it belongs to the given owning item and carries
    /// one of the re-checking source's id prefixes. Scoping by both is what keeps a re-check of one set
    /// (say, a collection) from disturbing a recommendation or a filmography entry the same owning item
    /// also seeded.
    /// </summary>
    /// <param name="item">The gap to test.</param>
    /// <param name="sourceItemId">The owning item whose gaps are being replaced.</param>
    /// <param name="idPrefixes">The gap-id prefixes the re-checking sources produce.</param>
    /// <returns>True when the gap is being replaced.</returns>
    public static bool IsReplaced(GapItem item, string sourceItemId, IReadOnlyCollection<string> idPrefixes)
    {
        if (!string.Equals(item.SourceItemId, sourceItemId, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var prefix in idPrefixes)
        {
            if (item.Id.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The distinct domains a set of gaps spans, for the caller to mark as dirty.
    /// </summary>
    /// <param name="items">The gaps.</param>
    /// <returns>The distinct domains.</returns>
    public static HashSet<MediaDomain> DomainsOf(IEnumerable<GapItem> items)
        => new(items.Select(i => i.Domain));

    /// <summary>
    /// Keeps an ad-hoc re-run of the same source from discarding a "where to watch" result the background
    /// pass already found for a gap (the lookup is the costly part); the rest comes fresh from the source.
    /// </summary>
    /// <param name="prior">The gap already in the report.</param>
    /// <param name="fresh">The freshly explored gap, mutated in place with the carried availability.</param>
    public static void CarryEnrichment(GapItem prior, GapItem fresh)
    {
        if (!prior.AvailabilityChecked)
        {
            return;
        }

        fresh.AvailabilityChecked = true;
        if (fresh.Availability.Count == 0 && prior.Availability.Count > 0)
        {
            fresh.Availability = prior.Availability;
        }
    }

    /// <summary>
    /// Copies the fields the availability pass produces from one report's items onto a (newer) report's
    /// items, matched by id. An item the newer report does not have (resolved or acquired since) is
    /// skipped; one it has that the pass did not touch keeps its values.
    /// </summary>
    /// <param name="from">The report the availability pass enriched.</param>
    /// <param name="into">The newer report to merge the enrichment into, mutated in place.</param>
    public static void MergeAvailability(GapReport from, GapReport into)
    {
        var byId = new Dictionary<string, GapItem>(StringComparer.Ordinal);
        foreach (var item in from.Items)
        {
            byId[item.Id] = item;
        }

        foreach (var target in into.Items)
        {
            if (!byId.TryGetValue(target.Id, out var source))
            {
                continue;
            }

            target.AvailabilityChecked = source.AvailabilityChecked;
            if (source.Availability.Count > 0)
            {
                target.Availability = source.Availability;
            }

            target.ProviderIds = source.ProviderIds;
            target.Links = source.Links;
        }
    }
}
