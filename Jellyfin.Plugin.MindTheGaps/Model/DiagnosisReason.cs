namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// The likely reason a gap is reported missing, so the dashboard can lead with a verdict instead of leaving
/// the reader to compare ids. Domain-agnostic: the same verdicts apply to a movie, show, album, or book once
/// each domain's matching is wired up. Set-level reasons (a misclassified set id, members that disagree on
/// the set id) arrive with the set-aware pass.
/// </summary>
public enum DiagnosisReason
{
    /// <summary>
    /// Not yet evaluated.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Nothing owned matches: it looks like a real gap.
    /// </summary>
    NotOwned,

    /// <summary>
    /// An owned item matches by title or a secondary id, but its primary (TheMovieDb) id differs or is
    /// missing, so the ownership diff cannot see it. The common "false missing" case.
    /// </summary>
    OwnedUnderWrongId,

    /// <summary>
    /// An owned item already carries this gap's id but under a different title, so one of them is
    /// misidentified.
    /// </summary>
    CarriesAnothersId,

    /// <summary>
    /// An owned item has this exact title and id, so the gap looks stale and a rescan should clear it.
    /// </summary>
    Stale,

    /// <summary>
    /// An id is the wrong class for its provider slot (for example an IMDb person id where a title id
    /// belongs), so the match never had a chance.
    /// </summary>
    WrongIdClass
}
