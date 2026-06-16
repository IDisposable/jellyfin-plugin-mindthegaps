namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The universal shape of a gap, independent of media domain.
/// </summary>
public enum GapPattern
{
    /// <summary>
    /// A known member of an owned container is missing
    /// (movie collection/franchise, series episodes, discography, book series, ...).
    /// </summary>
    SetCompletion = 0,

    /// <summary>
    /// A work by an owned creator is missing
    /// (filmography, discography, bibliography: actor/director/artist/author output).
    /// </summary>
    CreatorWorks = 1,

    /// <summary>
    /// A related/recommended title for discovery (not strictly a gap).
    /// </summary>
    Recommendation = 2
}
