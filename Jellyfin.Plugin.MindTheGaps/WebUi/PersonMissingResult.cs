using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.WebUi;

/// <summary>
/// The unowned filmography of one library person, split by media type, as the person page renders it.
/// </summary>
public sealed class PersonMissingResult
{
    /// <summary>
    /// Gets or sets the Jellyfin person id.
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// Gets or sets the person's name.
    /// </summary>
    public string PersonName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the person's TMDB id, when the library person carries one.
    /// </summary>
    public int? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the caller may send titles to Radarr/Sonarr: an administrator
    /// with at least one of them configured. The page shows Send buttons only when this is true.
    /// </summary>
    public bool CanSend { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a movie can be sent (Radarr configured).
    /// </summary>
    public bool CanSendMovies { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a series can be sent (Sonarr configured).
    /// </summary>
    public bool CanSendSeries { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the caller may keep a want-to-watch list: want to watch is on
    /// and the caller is a signed-in user without a parental rating limit. The page shows a bookmark on each
    /// card when it is.
    /// </summary>
    public bool CanTodo { get; set; }

    /// <summary>
    /// Gets or sets why the lists are empty when they could not be computed (no TMDB id on the person, TMDB
    /// has no record), so the page can say so instead of showing nothing. Null when the lists are real.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets the unowned movies, newest first.
    /// </summary>
    public IReadOnlyList<MissingTitle> Movies { get; set; } = Array.Empty<MissingTitle>();

    /// <summary>
    /// Gets or sets the unowned series, newest first.
    /// </summary>
    public IReadOnlyList<MissingTitle> Series { get; set; } = Array.Empty<MissingTitle>();
}
