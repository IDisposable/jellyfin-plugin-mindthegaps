using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// One row in a gap diagnosis: either the gap itself (the missing title) or an owned item that looks like
/// it should be the gap. Carries the provider ids so the dashboard can link each out and compare them.
/// </summary>
public sealed class DiagnosisItem
{
    /// <summary>
    /// Gets or sets the owned item's id (N-format guid) for an "open in Jellyfin" jump; null for the gap.
    /// </summary>
    public string? JellyfinItemId { get; set; }

    /// <summary>
    /// Gets or sets how this row relates to the gap: "target" (the gap itself), "titleMatch" (an owned
    /// item with the same title), or "idHolder" (an owned item already carrying the gap's id).
    /// </summary>
    public string Relation { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the year, if known.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets a short note on this row (for example "no TheMovieDb id" or "probably misidentified").
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Gets or sets the external ids this row carries (provider to id), mirroring <c>GapItem.ProviderIds</c>
    /// so the diagnosis is provider-agnostic; the dashboard compares whatever ids are present, not a fixed set.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets the external provider links for this row's ids, built by <c>ProviderLinks</c> so the
    /// dashboard links each id out using the one canonical set of URLs (no hand-rolled links client-side).
    /// </summary>
    public IReadOnlyList<ExternalLink> Links { get; set; } = [];
}
