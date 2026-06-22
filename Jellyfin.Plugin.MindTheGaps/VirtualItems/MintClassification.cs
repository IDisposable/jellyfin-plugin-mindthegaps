using System;

namespace Jellyfin.Plugin.MindTheGaps.VirtualItems;

/// <summary>
/// The outcome of classifying a gap for minting: whether it can be materialized as a virtual item today, and
/// if so, which container it belongs in, its TMDB id, and the person (if any) to attach. Pure data, so the
/// per-row, multi-select, bulk, and scheduled mint paths share one decision instead of repeating the guards.
/// </summary>
public sealed class MintClassification
{
    private MintClassification(bool isMaterializable, MintContainerKind containerKind, string? tmdbId, string? personName, string reason)
    {
        IsMaterializable = isMaterializable;
        ContainerKind = containerKind;
        TmdbId = tmdbId;
        PersonName = personName;
        Reason = reason;
    }

    /// <summary>
    /// Gets a value indicating whether the gap can be minted today.
    /// </summary>
    public bool IsMaterializable { get; }

    /// <summary>
    /// Gets the container the placeholder belongs in (meaningful only when materializable).
    /// </summary>
    public MintContainerKind ContainerKind { get; }

    /// <summary>
    /// Gets the gap's TMDB id (non-null when materializable).
    /// </summary>
    public string? TmdbId { get; }

    /// <summary>
    /// Gets the person to attach (a filmography gap's owner), or null.
    /// </summary>
    public string? PersonName { get; }

    /// <summary>
    /// Gets the human-readable reason, used as the skip message when not materializable.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Creates a non-materializable result with the reason it cannot be minted.
    /// </summary>
    /// <param name="reason">Why the gap cannot be minted.</param>
    /// <returns>A non-materializable classification.</returns>
    public static MintClassification NotMaterializable(string reason)
        => new(false, MintContainerKind.CatchAllCollection, null, null, reason);

    /// <summary>
    /// Creates a materializable result.
    /// </summary>
    /// <param name="containerKind">The container the placeholder belongs in.</param>
    /// <param name="tmdbId">The gap's TMDB id (required).</param>
    /// <param name="personName">The person to attach, or null.</param>
    /// <returns>A materializable classification.</returns>
    public static MintClassification Materializable(MintContainerKind containerKind, string tmdbId, string? personName)
    {
        if (string.IsNullOrEmpty(tmdbId))
        {
            throw new ArgumentException("A materializable gap must have a TMDB id.", nameof(tmdbId));
        }

        return new MintClassification(true, containerKind, tmdbId, personName, "Materializable.");
    }
}
