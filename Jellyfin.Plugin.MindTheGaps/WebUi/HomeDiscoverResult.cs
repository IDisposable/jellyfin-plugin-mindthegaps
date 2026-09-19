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
    /// Gets or sets a value indicating whether the caller may add a title to their personal todo list: an
    /// administrator, regardless of whether Radarr/Sonarr are configured. The row falls back to this when
    /// the matching CanSend flag is false, so an admin who has not set up an arr yet still has something
    /// actionable besides the TMDB link.
    /// </summary>
    public bool CanTodo { get; set; }

    /// <summary>
    /// Gets or sets the ranked titles.
    /// </summary>
    public IReadOnlyList<MissingTitle> Titles { get; set; } = Array.Empty<MissingTitle>();
}
