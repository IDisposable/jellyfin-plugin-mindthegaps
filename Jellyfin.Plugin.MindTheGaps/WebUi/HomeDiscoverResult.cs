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
    /// Gets or sets the ranked titles.
    /// </summary>
    public IReadOnlyList<MissingTitle> Titles { get; set; } = Array.Empty<MissingTitle>();
}
