using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The albums an owned artist made, or the books an owned book's author wrote, that the library does not
/// hold, as the artist and book pages render them.
/// </summary>
public sealed class WorksMissingResult
{
    /// <summary>
    /// Gets or sets the Jellyfin item id of the owned artist or book.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the owned item's name.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind of work listed: <c>MusicAlbum</c> on an artist page, <c>Book</c> on a book page.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the caller may keep a want-to-watch list: want to watch is on
    /// and the caller is a signed-in user without a parental rating limit. Music and books have no download
    /// handoff, so the list is the only action.
    /// </summary>
    public bool CanTodo { get; set; }

    /// <summary>
    /// Gets or sets why the list is empty when it could not be computed (the provider did not answer, or the
    /// item carries no id to look it up by). Null when the list is real, including when it is empty because
    /// nothing is missing.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets the unowned works, newest first.
    /// </summary>
    public IReadOnlyList<MissingWork> Works { get; set; } = Array.Empty<MissingWork>();
}
