namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The media domain a gap belongs to (maps loosely to Jellyfin's CollectionType).
/// </summary>
public enum MediaDomain
{
    /// <summary>
    /// Movies and shows.
    /// </summary>
    Video = 0,

    /// <summary>
    /// Music: artists, albums, music videos.
    /// </summary>
    Music = 1,

    /// <summary>
    /// Books and audiobooks.
    /// </summary>
    Book = 2
}
