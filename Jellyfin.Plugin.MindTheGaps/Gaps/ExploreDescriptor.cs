using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Describes one chip-pickable explore "kind" a source exposes: how to run the source for an explicit set of
/// picked ids, and (optionally) how to search for sets to pick and resolve a picked id to a display name. The
/// engine, the API, and the dashboard derive the supported kinds, the run seam, and the type-ahead from these,
/// so adding a kind is a source declaring one rather than editing the engine, the controller, and the dropdown
/// by hand.
/// </summary>
public sealed class ExploreDescriptor
{
    /// <summary>
    /// Gets the kind token (case-insensitive), for example "studio" or "mdblist".
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Gets the human label shown in the explore dropdown, for example "Studio".
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Gets the source that produces this kind's gaps, used for its owned kinds and name when running.
    /// </summary>
    public required IGapSource Source { get; init; }

    /// <summary>
    /// Gets the run seam: streams the gaps for the picked ids, diffed against the context's ownership index.
    /// </summary>
    public required Func<GapScanContext, IReadOnlyList<int>, CancellationToken, IAsyncEnumerable<GapItem>> Run { get; init; }

    /// <summary>
    /// Gets the type-ahead search for the chip picker, or <see langword="null"/> when the provider has no
    /// search (a TMDB list, entered by raw id).
    /// </summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<CuratedSetRef>>>? Search { get; init; }

    /// <summary>
    /// Gets the id-to-name resolve used to render a saved chip, or <see langword="null"/> when the kind is
    /// entered by raw id.
    /// </summary>
    public Func<int, CancellationToken, Task<string?>>? Resolve { get; init; }
}
