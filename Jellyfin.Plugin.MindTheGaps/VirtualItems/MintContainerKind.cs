namespace Jellyfin.Plugin.MindTheGaps.VirtualItems;

/// <summary>
/// Where a materializable gap's virtual placeholder belongs.
/// </summary>
public enum MintContainerKind
{
    /// <summary>
    /// The gap names an owning BoxSet (a collection gap); the placeholder goes into that BoxSet.
    /// </summary>
    OwningBoxSet = 0,

    /// <summary>
    /// The gap has no owning BoxSet (a filmography or recommendation gap); the placeholder goes into the
    /// shared catch-all collection.
    /// </summary>
    CatchAllCollection = 1
}
