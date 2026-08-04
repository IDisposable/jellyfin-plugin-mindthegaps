namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// A source whose gaps section the Discover tab: the personal want-lists, the lists the user pointed the
/// plugin at, and the per-title recommender. Declaring the kind lets the engine record what each one did on
/// a scan (see <see cref="Model.SourceRun"/>) even when it produced nothing.
/// </summary>
internal interface IDiscoverSource
{
    /// <summary>
    /// Gets the discovery kind its gaps carry as their <see cref="Model.GapItem.SourceItemType"/>, which is
    /// the section they file under.
    /// </summary>
    string DiscoverKind { get; }
}
