using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MindTheGaps.Model;

/// <summary>
/// A persisted snapshot of a gap the user added to their personal todo list of things to acquire. It
/// holds just enough of the gap to render, search for, link, and verify the title later, so an entry
/// survives rescans even if the report no longer carries that gap.
/// </summary>
public class TodoEntry
{
    /// <summary>
    /// Gets or sets the gap's stable id (the key the entry is upserted by).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release year, if known.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the media domain name (Movies, Shows, Music, Books).
    /// </summary>
    public string DomainName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target item-kind name (Movie, Series, Episode, MusicAlbum, Book, ...).
    /// </summary>
    public string TargetKindName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the gap pattern name (SetCompletion, CreatorWorks, Recommendation). The list needs it
    /// to say what <see cref="Creator"/> means, since the same field holds a series, a recommending title,
    /// a creator, or a set depending on the pattern. Empty on an entry persisted without it.
    /// </summary>
    public string PatternName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the author, artist, creator, or source that surfaced the gap (the gap's source item
    /// name). Null when the gap carried no owning source.
    /// </summary>
    public string? Creator { get; set; }

    /// <summary>
    /// Gets or sets the gap's provider ids (e.g. {"Tmdb":"603","Imdb":"tt0133093"}), used to verify the
    /// entry against the library.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets the gap's external links (TMDB, JustWatch, Trakt, ...).
    /// </summary>
    public IReadOnlyList<ExternalLink> Links { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the entry is done (acquired, or marked done by the user).
    /// </summary>
    public bool Done { get; set; }

    /// <summary>
    /// Gets or sets when the entry was added (ISO 8601 UTC string).
    /// </summary>
    public string AddedUtc { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the entry was marked done (ISO 8601 UTC string), or null while not done.
    /// </summary>
    public string? DoneUtc { get; set; }
}
