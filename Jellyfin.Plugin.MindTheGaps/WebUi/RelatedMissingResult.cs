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
    /// Gets or sets a value indicating whether the caller may keep a want-to-watch list: want to watch is on
    /// and the caller is a signed-in user without a parental rating limit. The page shows a bookmark on each
    /// card when it is.
    /// </summary>
    public bool CanTodo { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the caller may say they are not interested in a title: the
    /// feature is on and the caller is a signed-in user. The page offers "Not interested" when it is.
    /// </summary>
    public bool CanHide { get; set; }

    /// <summary>
    /// Gets or sets how many titles the caller is not interested in; 0 when they cannot keep the list.
    /// </summary>
    public int NotInterestedCount { get; set; }

    /// <summary>
    /// Gets or sets where the row goes on the page: a side and one of jellyfin-web's own item page sections,
    /// such as <c>after:similar</c>.
    /// </summary>
    public string Placement { get; set; } = ItemPlacement.Default;

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
