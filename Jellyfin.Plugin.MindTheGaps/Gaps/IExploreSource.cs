using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// An <see cref="IGapSource"/> that also exposes one or more chip-pickable explore kinds, each runnable ad-hoc
/// for a picked set of ids. Implemented by the curated and discovery sources (TMDB curated sets, Discogs
/// labels, MDBList lists); the owned-derived sources (collections, filmographies, series content) do not.
/// </summary>
internal interface IExploreSource
{
    /// <summary>
    /// Gets the explore kinds this source exposes.
    /// </summary>
    IReadOnlyCollection<ExploreDescriptor> ExploreDescriptors { get; }
}
