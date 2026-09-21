using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The home screen's discovery row: the recommendation gaps the scan has accumulated, ranked.
/// </summary>
public sealed class HomeDiscoverResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the caller may send a movie (administrator, Radarr configured).
    /// </summary>
    public bool CanSendMovies { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the caller may send a series (administrator, Sonarr configured).
    /// </summary>
    public bool CanSendSeries { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the caller may keep a want-to-watch list: want to watch is on
    /// and the caller is a signed-in user without a parental rating limit. The row shows a bookmark on each
    /// card when it is.
    /// </summary>
    public bool CanTodo { get; set; }

    /// <summary>
    /// Gets or sets the ranked titles.
    /// </summary>
    public IReadOnlyList<MissingTitle> Titles { get; set; } = Array.Empty<MissingTitle>();
}
