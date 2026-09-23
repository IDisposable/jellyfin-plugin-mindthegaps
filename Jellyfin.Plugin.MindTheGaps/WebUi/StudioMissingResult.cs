using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The unowned movies from a studio, as jellyfin-web's own generic list page (routed by <c>studioId</c>)
/// renders them under "Missing from &lt;studio&gt;".
/// </summary>
public sealed class StudioMissingResult
{
    /// <summary>
    /// Gets or sets the Jellyfin studio item id.
    /// </summary>
    public Guid StudioId { get; set; }

    /// <summary>
    /// Gets or sets the studio's name.
    /// </summary>
    public string StudioName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the caller may keep a want-to-watch list: want to watch is on
    /// and the caller is a signed-in user without a parental rating limit. The page shows a bookmark on each
    /// card when it is.
    /// </summary>
    public bool CanTodo { get; set; }

    /// <summary>
    /// Gets or sets why the list is empty when it could not be computed (the studio name could not be
    /// matched to a TMDB company). Null when the list is real.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets the studio's unowned movies, newest first.
    /// </summary>
    public IReadOnlyList<MissingTitle> Titles { get; set; } = Array.Empty<MissingTitle>();
}
