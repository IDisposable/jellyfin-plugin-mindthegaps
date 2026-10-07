using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The home screen's discovery row: the recommendation gaps the scan has accumulated, ranked.
/// </summary>
public sealed class HomeDiscoverResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the caller may keep a want-to-watch list: want to watch is on
    /// and the caller is a signed-in user without a parental rating limit. The row shows a bookmark on each
    /// card when it is.
    /// </summary>
    public bool CanTodo { get; set; }

    /// <summary>
    /// Gets or sets where the row goes on the home screen: empty for the bottom, <c>top</c>, or the home
    /// section type it follows.
    /// </summary>
    public string Placement { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the caller's home slots in order, one section type each, when <see cref="Placement"/>
    /// follows a section type; null otherwise.
    /// </summary>
    public IReadOnlyList<string>? HomeSections { get; set; }

    /// <summary>
    /// Gets or sets the ranked titles.
    /// </summary>
    public IReadOnlyList<MissingTitle> Titles { get; set; } = Array.Empty<MissingTitle>();
}
