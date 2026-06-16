using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Configuration;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// Read-only, domain-agnostic snapshot handed to each gap source for a single scan.
/// </summary>
public sealed class GapScanContext
{
    /// <summary>
    /// The provider name Jellyfin uses for TMDB ids.
    /// </summary>
    public const string TmdbProvider = "Tmdb";

    private Action<double>? _progressSink;

    /// <summary>
    /// Initializes a new instance of the <see cref="GapScanContext"/> class.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="ownership">The ownership index.</param>
    public GapScanContext(PluginConfiguration config, OwnershipIndex ownership)
    {
        Config = config;
        Ownership = ownership;
    }

    /// <summary>
    /// Gets the plugin configuration for this scan.
    /// </summary>
    public PluginConfiguration Config { get; }

    /// <summary>
    /// Gets the ownership index.
    /// </summary>
    public OwnershipIndex Ownership { get; }

    /// <summary>
    /// Sets the sink the engine uses to fold the current source's 0..1 progress into the overall scan
    /// bar. Set per source by the engine.
    /// </summary>
    /// <param name="sink">The sink, or null to clear it.</param>
    internal void SetProgressSink(Action<double>? sink) => _progressSink = sink;

    /// <summary>
    /// Reports the current source's progress through its own work, from 0 to 1. Sources that finish
    /// quickly need not call this; the engine still advances the bar when each source completes.
    /// </summary>
    /// <param name="fraction">Completion of the current source, from 0 to 1.</param>
    public void ReportProgress(double fraction) => _progressSink?.Invoke(fraction);

    /// <summary>
    /// Determines whether an item of the given kind is owned under ANY of the supplied provider ids.
    /// This is the single, domain-agnostic ownership check; there are intentionally no
    /// per-media-type helpers.
    /// </summary>
    /// <param name="kind">The item kind.</param>
    /// <param name="providerIds">The candidate's provider ids.</param>
    /// <returns><see langword="true"/> if owned under any id.</returns>
    public bool IsOwned(BaseItemKind kind, IReadOnlyDictionary<string, string> providerIds)
        => Ownership.OwnsAny(kind, providerIds);
}
