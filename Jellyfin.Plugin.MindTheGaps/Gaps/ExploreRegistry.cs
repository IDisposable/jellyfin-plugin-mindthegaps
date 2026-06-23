using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Aggregates the chip-pickable explore kinds every source declares, so the engine, the API, and the dashboard
/// share one derived list rather than each hard-coding the kinds and their search/resolve/run wiring. Built
/// once from the registered sources.
/// </summary>
public sealed class ExploreRegistry
{
    private readonly Dictionary<string, ExploreDescriptor> _byKind;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExploreRegistry"/> class.
    /// </summary>
    /// <param name="sources">The registered gap sources; the explore-capable ones contribute their kinds.</param>
    public ExploreRegistry(IEnumerable<IGapSource> sources)
    {
        var byKind = new Dictionary<string, ExploreDescriptor>(StringComparer.OrdinalIgnoreCase);
        var kinds = new List<ExploreKindInfo>();
        foreach (var descriptor in sources.OfType<IExploreSource>().SelectMany(s => s.ExploreDescriptors))
        {
            if (byKind.TryAdd(descriptor.Kind, descriptor))
            {
                kinds.Add(new ExploreKindInfo
                {
                    Kind = descriptor.Kind,
                    Label = descriptor.Label,
                    Searchable = descriptor.Search is not null
                });
            }
        }

        _byKind = byKind;
        Kinds = kinds;
    }

    /// <summary>
    /// Gets the supported explore kinds (token, label, and whether searchable), for the dropdown.
    /// </summary>
    public IReadOnlyList<ExploreKindInfo> Kinds { get; }

    /// <summary>
    /// Gets the supported kind tokens, for an error message listing them.
    /// </summary>
    public IReadOnlyCollection<string> KindTokens => _byKind.Keys;

    /// <summary>
    /// Determines whether the given kind is a supported explore kind.
    /// </summary>
    /// <param name="kind">The kind token (case-insensitive), or null.</param>
    /// <returns><see langword="true"/> if known.</returns>
    public bool IsKnown(string? kind) => kind is not null && _byKind.ContainsKey(kind);

    /// <summary>
    /// Finds the descriptor for a kind, or <see langword="null"/> when the kind is unknown.
    /// </summary>
    /// <param name="kind">The kind token (case-insensitive), or null.</param>
    /// <returns>The descriptor, or null.</returns>
    public ExploreDescriptor? Find(string? kind)
        => kind is not null && _byKind.TryGetValue(kind, out var descriptor) ? descriptor : null;
}
