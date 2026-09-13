namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// Where a card on a web UI surface came from, so the detail and Send endpoints can rehydrate its gap from
/// the same place instead of trusting a client-posted gap.
/// </summary>
public enum GapSource
{
    /// <summary>
    /// A person page: the gap is one of the person's filmography credits, recomputed from TMDB.
    /// </summary>
    Person,

    /// <summary>
    /// A movie or series page: the gap is one of the title's similar titles, recomputed from TMDB.
    /// </summary>
    Item,

    /// <summary>
    /// The home screen: the gap is a recommendation in the scanned report.
    /// </summary>
    Home,

    /// <summary>
    /// The want-to-watch row: the gap is an entry on the todo list, rebuilt from its snapshot.
    /// </summary>
    Todo
}
