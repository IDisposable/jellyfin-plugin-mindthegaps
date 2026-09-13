using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The unowned titles similar to one owned movie or series, as the item page renders them under "More like
/// this you don't have".
/// </summary>
public sealed class RelatedMissingResult
{
    /// <summary>
    /// Gets or sets the Jellyfin item id of the owned title.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the owned title's name.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the caller may send a title to Radarr/Sonarr for this kind of
    /// item: an administrator with the matching target configured.
    /// </summary>
    public bool CanSend { get; set; }

    /// <summary>
    /// Gets or sets why the list is empty when it could not be computed (no TMDB id on the item). Null when
    /// the list is real.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets the unowned similar titles, in TMDB's order of similarity.
    /// </summary>
    public IReadOnlyList<MissingTitle> Titles { get; set; } = Array.Empty<MissingTitle>();
}
