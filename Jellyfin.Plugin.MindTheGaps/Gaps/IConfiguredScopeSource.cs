using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// A source whose scope is entirely re-derived from configuration every scan (an id list, an account
/// connection, a toggle) rather than rotated through a stable candidate set. Implemented by every such
/// source so <see cref="StaleOwnerPruner"/> can tell "no longer configured" from "just not this run's
/// turn yet" (which is what a rotating source's carried-forward gap means, and must not be pruned for).
/// </summary>
internal interface IConfiguredScopeSource
{
    /// <summary>
    /// Gets the gap-id prefix this source's gaps carry, so a prune only ever asks this source about a gap
    /// it could plausibly have produced.
    /// </summary>
    string GapIdPrefix { get; }

    /// <summary>
    /// Determines, purely from <paramref name="config"/> (no I/O, so this can never go wrong from a
    /// transient failure the way asking a live provider "does this still exist" could), whether a gap is
    /// still within this source's current scope. Only meaningful for a gap whose id starts with
    /// <see cref="GapIdPrefix"/>; the result is unspecified otherwise.
    /// </summary>
    /// <param name="item">The persisted gap.</param>
    /// <param name="config">The current configuration.</param>
    /// <returns><see langword="true"/> to keep it; <see langword="false"/> when it should be pruned.</returns>
    bool StillInScope(GapItem item, PluginConfiguration config);
}
