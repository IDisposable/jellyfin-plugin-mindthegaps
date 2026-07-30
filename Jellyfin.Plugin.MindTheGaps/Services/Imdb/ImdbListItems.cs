using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Services.Imdb;

/// <summary>
/// One page of a list's entries.
/// </summary>
internal sealed class ImdbListItems
{
    /// <summary>
    /// Gets or sets how many entries the whole list holds.
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Gets or sets the pagination state.
    /// </summary>
    public ImdbPageInfo? PageInfo { get; set; }

    /// <summary>
    /// Gets or sets this page's entries.
    /// </summary>
    public IReadOnlyList<ImdbListEdge>? Edges { get; set; }
}
