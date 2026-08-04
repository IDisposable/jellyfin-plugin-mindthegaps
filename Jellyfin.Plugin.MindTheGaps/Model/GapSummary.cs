using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A lightweight overview of the report: the per-pattern gap counts and the streaming providers seen,
/// without the gap items themselves. The dashboard loads this to render the pattern tabs and seed the
/// provider filter, then fetches one pattern's items at a time, so a large report is not shipped whole.
/// </summary>
public class GapSummary
{
    /// <summary>
    /// Gets or sets the UTC time the report was generated.
    /// </summary>
    public DateTime GeneratedUtc { get; set; }

    /// <summary>
    /// Gets or sets the plugin version that generated the report.
    /// </summary>
    public string GeneratedVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total number of gaps across every pattern.
    /// </summary>
    public int TotalGaps { get; set; }

    /// <summary>
    /// Gets or sets the gap count per pattern, keyed by the pattern name (matching
    /// <see cref="GapItem.PatternName"/>), so the dashboard can label and order the tabs.
    /// </summary>
    public IReadOnlyDictionary<string, int> PatternCounts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the gap patterns in tab order, so the dashboard renders its tabs from the model's own
    /// vocabulary rather than a copy of it.
    /// </summary>
    public IReadOnlyList<string> Patterns { get; set; } = [];

    /// <summary>
    /// Gets or sets the media domains worth offering, in display order (see
    /// <see cref="MediaDomains.Implemented"/>). A domain the model names but nothing fills is left out, so
    /// the Type selector never offers one that can only be empty.
    /// </summary>
    public IReadOnlyList<string> Domains { get; set; } = [];

    /// <summary>
    /// Gets or sets the Set completion group kinds in display order (see
    /// <see cref="SourceItemTypes.SetKindsInOrder"/>), matching <see cref="GapItem.SourceItemType"/>. The
    /// dashboard supplies the wording for each; the set and its order come from here.
    /// </summary>
    public IReadOnlyList<string> SetKinds { get; set; } = [];

    /// <summary>
    /// Gets or sets the Discover section kinds in display order (see
    /// <see cref="SourceItemTypes.DiscoverKindsInOrder"/>), matching <see cref="GapItem.SourceItemType"/>.
    /// Same contract as <see cref="SetKinds"/>: the dashboard supplies the wording, the set and its order
    /// come from here.
    /// </summary>
    public IReadOnlyList<string> DiscoverKinds { get; set; } = [];

    /// <summary>
    /// Gets or sets the gap-id prefixes whose owning item can be re-checked against its provider on its own.
    /// A row qualifies when its id starts with one of these, which is exact where inferring it from the
    /// pattern and domain was not, and follows the enabled sources so a switched-off source stops offering.
    /// </summary>
    public IReadOnlyList<string> RecheckPrefixes { get; set; } = [];

    /// <summary>
    /// Gets or sets the kinds that can be minted as virtual placeholders, each mapped to the provider id a
    /// gap must carry to be mintable as that kind, so the dashboard offers Mint exactly where the minter
    /// would accept it.
    /// </summary>
    public IReadOnlyDictionary<string, string> MintableKinds { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the distinct streaming-provider names present anywhere in the report's availability,
    /// so the provider filter is fully populated before the per-pattern items load.
    /// </summary>
    public IReadOnlyList<string> Providers { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether availability ("where to watch") is enabled in config.
    /// </summary>
    public bool AvailabilityEnabled { get; set; }

    /// <summary>
    /// Gets or sets how many distinct watch targets still need a "where to watch" lookup, so the
    /// dashboard can show the remaining backlog on its button and indicate when it is cleared.
    /// </summary>
    public int AvailabilityPending { get; set; }
}
