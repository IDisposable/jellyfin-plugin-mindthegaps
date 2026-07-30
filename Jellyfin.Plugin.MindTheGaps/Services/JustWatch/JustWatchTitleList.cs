using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Services.JustWatch;

/// <summary>
/// One page of a list.
/// </summary>
internal sealed class JustWatchTitleList
{
    /// <summary>
    /// Gets or sets how many entries the whole list holds.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the pagination state.
    /// </summary>
    public JustWatchPageInfo? PageInfo { get; set; }

    /// <summary>
    /// Gets or sets this page's entries.
    /// </summary>
    public IReadOnlyList<JustWatchTitleEdge>? Edges { get; set; }
}
