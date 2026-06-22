using System;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MindTheGaps.Model;

namespace Jellyfin.Plugin.MindTheGaps.VirtualItems;

/// <summary>
/// Decides whether a gap can be minted as a virtual movie today and, if so, which container it belongs in
/// and which person to attach. Pure and library-free, so every mint path (per-row, multi-select, bulk,
/// scheduled) shares the same rule. Today only Movie gaps with a TMDB id are materializable; episodes are
/// materialized natively by the host, and other kinds are not minted yet.
/// </summary>
public static class MintClassifier
{
    /// <summary>
    /// Classifies a gap for minting.
    /// </summary>
    /// <param name="gap">The gap to classify.</param>
    /// <returns>The classification.</returns>
    public static MintClassification Classify(GapItem gap)
    {
        ArgumentNullException.ThrowIfNull(gap);

        if (gap.TargetKind == BaseItemKind.Episode)
        {
            return MintClassification.NotMaterializable("Episodes are materialized natively by the server, not minted.");
        }

        if (gap.TargetKind != BaseItemKind.Movie)
        {
            return MintClassification.NotMaterializable(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Only Movie gaps can be minted today; '{0}' is a {1}.", gap.Name, gap.TargetKind));
        }

        if (!TryGetTmdbId(gap, out var tmdbId))
        {
            return MintClassification.NotMaterializable(string.Format(System.Globalization.CultureInfo.InvariantCulture, "'{0}' has no TMDB id; cannot mint.", gap.Name));
        }

        // A collection gap goes into its owning BoxSet; a filmography or recommendation gap goes into the
        // shared catch-all collection, and a filmography (Person) gap also attaches the person so the minted
        // movie surfaces on that person's page.
        var containerKind = string.Equals(gap.SourceItemType, "BoxSet", StringComparison.Ordinal)
            ? MintContainerKind.OwningBoxSet
            : MintContainerKind.CatchAllCollection;
        var personName = string.Equals(gap.SourceItemType, "Person", StringComparison.Ordinal) ? gap.SourceItemName : null;

        return MintClassification.Materializable(containerKind, tmdbId, personName);
    }

    private static bool TryGetTmdbId(GapItem gap, out string tmdbId)
    {
        if (gap.ProviderIds.TryGetValue("Tmdb", out var id) && !string.IsNullOrEmpty(id))
        {
            tmdbId = id;
            return true;
        }

        tmdbId = string.Empty;
        return false;
    }
}
