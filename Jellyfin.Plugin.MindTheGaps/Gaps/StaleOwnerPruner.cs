using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Finds gaps a <see cref="IConfiguredScopeSource"/> produced in the past but would not produce today,
/// given the current configuration (a removed keyword id, a disabled watchlist, ...). Pure and standalone
/// so it is unit-testable without a scan: <see cref="Gaps.GapEngine"/> has no seams for an end-to-end test
/// of its own, but this needs none, since the decision only ever depends on the gaps and the config.
/// </summary>
internal static class StaleOwnerPruner
{
    /// <summary>
    /// Finds the ids of gaps that are no longer within any configured scope source's current scope.
    /// </summary>
    /// <param name="items">The gaps to check.</param>
    /// <param name="scopedSources">Every non-rotating, config-scoped source.</param>
    /// <param name="config">The current configuration.</param>
    /// <returns>The stale gap ids. A set, not a list: the caller only ever needs membership (a lookup
    /// while filtering the report, or as the removal set), and a gap id cannot appear twice.</returns>
    public static IReadOnlySet<string> FindStaleIds(
        IEnumerable<GapItem> items,
        IEnumerable<IConfiguredScopeSource> scopedSources,
        PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(scopedSources);
        ArgumentNullException.ThrowIfNull(config);

        var sources = scopedSources is IReadOnlyList<IConfiguredScopeSource> list ? list : new List<IConfiguredScopeSource>(scopedSources);
        var stale = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            foreach (var source in sources)
            {
                if (!item.Id.StartsWith(source.GapIdPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!source.StillInScope(item, config))
                {
                    stale.Add(item.Id);
                }

                // A gap id carries exactly one owning prefix; no other scoped source needs asking.
                break;
            }
        }

        return stale;
    }
}
