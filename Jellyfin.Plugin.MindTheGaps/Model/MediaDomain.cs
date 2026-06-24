namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The media domain a gap belongs to (maps loosely to Jellyfin's library/CollectionType), split by
/// the way users actually organize libraries rather than the broad "video" bucket.
/// </summary>
public enum MediaDomain
{
    /// <summary>
    /// Movies.
    /// </summary>
    Movies = 0,

    /// <summary>
    /// Series, seasons, and episodes.
    /// </summary>
    Shows = 1,

    /// <summary>
    /// Music: artists, albums, tracks.
    /// </summary>
    Music = 2,

    /// <summary>
    /// Books and audiobooks.
    /// </summary>
    Books = 3,

    /// <summary>
    /// Music videos.
    /// </summary>
    MusicVideos = 4
}
